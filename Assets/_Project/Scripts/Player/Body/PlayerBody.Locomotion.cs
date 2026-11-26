using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// Ground locomotion, from walk and run cycles each measured on this body as it is made (GaitClips: how fast its feet carry
/// the body, which way, where the left foot comes down), so it fits any character and any cycle without numbers per clip:
/// - Each way of travel (ahead, right, back, left) lists its cycles; the speed blends the two either side of it by their
///   measured speeds. The two ways either side of the heading share the weight so the blended feet push straight back
///   along the travel: a slow side cycle takes more of a diagonal than a fast forward one, so neither skates sideways.
/// - Every cycle shares one step: each stride starts with the left foot down, wherever its file starts or however many
///   strides it holds.
/// - The legs carry the body (0.9.415): the player's own body goes over the ground as far and the way its cycles' feet carry it
///   at their own pace (LegsCarry; PlayerMovement.Stride is only what the legs are asked), so a foot that is down stays where it
///   came down with nothing holding it there. Another PC's body goes where the network has it, its cycles played at the pace
///   that carries it so, within gaitRate (Pace). Until 0.9.415 every body went at the keys' speed eased on its own, its cycles
///   followed what it was seen to do at a pace fitted to it, and where the two parted (a turn, a flick of the view, a start)
///   the feet were held, dragged and caught up one by one (FootPlanting): fifteen versions of fixes in a day (the user,
///   2026-11-25: "아무리 해결해도 문제가 발생하는 건 근본이 잘못돼서 그런 거 아니에요", "리스크를 짊어질 준비가 되었어").
///   Until 0.8.260 the cycles played at their own pace whatever the speed and the feet skated 30-60% of it (the user,
///   2026-11-12: "우스꽝스럽고 초보 같고 급하던데"), and until 0.9.350 the pace was a person's for the speed (gaitCadence),
///   which stepped BIPER's walks a tenth faster and shorter than they are made.
/// - Sprinting with a gun out the gun's run has the whole body (GunRuns); it goes ahead only, so the gun sprints ahead or
///   at a slant ahead (SprintsToward: W, W+A, W+D), the hips turned to the way it runs and the spine turning the chest
///   back to the view (SpineLook).
/// - With a gun out the idle is the gun's stance; aiming, the gun's aim cycles take over as far as they reach the speed.
/// - Crouched, the crouch cycles, facing the view.
/// Every PC works this out from the same speed and step (PlayerBody.Pace), so teammates see the same legs.
/// </summary>
public sealed partial class PlayerBody
{
    // The cycles of every stance are rows of Tables/Player/BodyMoves.csv (MoveSets), not fields: a set lists its cycles in
    // any order and the body measures which way and how fast each one goes (GaitClips).

    [Header("Stride")]
    [SerializeField, Tooltip("How much slower (x) and faster (y) than its own pace a cycle may play: another PC's to carry its body "
        + "as the network has it; the player's own only faster, for what the gait's speed is put above its cycles' (a class's pace).")]
    private Vector2 gaitRate = new Vector2(.85f, 1.2f);
    [SerializeField, Range(0f, 1f), Tooltip("Sprinting with a gun out from this far into the sprint (PlayerMovement.SprintProgress), the "
        + "gun's run has the whole body (GunRuns).")]
    private float gunRunFrom = .5f;

    [Header("Turning on the spot")]
    [SerializeField, Range(10f, 120f), Tooltip("Standing still, the legs stay planted until the view has turned this far "
        + "from them (degrees); the chest twists toward the view meanwhile. Then the legs step round.")]
    private float turnStartAngle = 60f;
    [SerializeField, Min(.05f), Tooltip("Turning on the spot, how long a step that turns the hips 45° takes (s); a step that turns "
        + "further takes longer, by the square root (TurnPlanner).")]
    private float turnStepTime = .3f;
    [SerializeField, Min(0f), Tooltip("How quickly the legs follow a change in the way and speed of travel (s). Quick A/D or "
        + "W taps even out instead of flipping the stride from one side to the other.")]
    private float strideFollowTime = .12f;

    // The table of the stances' cycles (Tables/Player/BodyMoves.csv): a row a set, its idle and its cycles. "plain" and
    // "crouch" stand on Body.idle and Body.crouchIdle; a gun's sets are "<kind>", "<kind>.aim", "<kind>.crouch" and
    // "<kind>.crouch.aim", and the kind row's ammo cell names the ammunition (Weapons.csv ammo) that takes them, "*" for any
    // the other kinds don't name.
    private const string MovesTable = "BodyMoves", PlainSetId = "plain", CrouchSetId = "crouch", AnyAmmunition = "*";
    // Cycles measured within this many degrees of each other go the same way: one way's walk, jog and run.
    private const float SameWay = 15f;
    // Two measured ways this far apart or less blend into every heading between them (a four-way set's are 90 degrees apart).
    private const float WaysJoin = 120f;
    // A way with no neighbour that near carries a heading within half an eight-way set's step of it, and none from a whole
    // step off: further round, its feet would push the body sideways off the travel, which the other set's cycles carry.
    private const float LoneWayReach = 22.5f, LoneWayEnd = 45f;

    // One way of travel in a set: where its cycles' feet carry the body (degrees, 0 ahead, 90 right), slowest cycle first.
    private sealed class Way
    {
        public float Angle;
        public readonly List<Channel> Cycles = new List<Channel>();
    }

    // A stance and its cycles, grouped by the way each was measured to go on this body, by angle round from ahead.
    private sealed class MoveSet
    {
        public Channel Idle;
        public readonly List<Way> Ways = new List<Way>();
    }

    // A gun kind's sets: held at the hip, aimed, and both crouched; any may be missing (null), and what a set can't carry
    // goes to the next set down, the plain or crouch set last.
    private sealed class GunSets
    {
        public MoveSet Hip, Aim, Crouch, CrouchAim;
    }

    private MoveSet plainSet, crouchSet;
    // The gun kinds' sets by kind, and the kind each ammunition takes (AnyAmmunition for the rest).
    private readonly Dictionary<string, GunSets> gunSets = new Dictionary<string, GunSets>(System.StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> kindOfAmmunition = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    // This frame's sets that may have the legs, first asked first, how much each wants, and what each was given.
    private readonly List<MoveSet> asking = new List<MoveSet>(4);
    private readonly List<float> wanting = new List<float>(4);
    // Sprinting with a gun out (GunRuns), the frame Locomotion last said so, and the sprint's progress then and whether it
    // was rising (SprintingNow).
    private bool gunRuns, sprintRising;
    private int gunRunsFrame = -10;
    private float sprintSeen;
    private float legYaw;
    // How the feet step a turn on the spot (TurnOnTheSpot, FootPlanting), and the frame it was last ticked.
    private readonly TurnPlanner turnPlanner = new TurnPlanner();
    private int plannedFrame = -10;
    private Vector3 stride;
    private int turning;
    private bool legsPlanted;
    // This body's leg, hip joint to ankle (m).
    private float legLength = .9f;
    // This frame's blend of cycles: their weight, weight by stride time (s) and by how far their feet carry the body in a stride
    // (m, in the body's own frame: x right, y ahead).
    private float mixWeight, mixStride;
    private Vector2 mixLength;
    // How the player's own legs carry the body over the ground (world, m/s), and the frame they last did (LegsCarry); whether
    // the movement asked them this frame (PlayerMovement.Stride), and the body's facing they were laid under (degrees off the
    // view).
    private Vector3 legsVelocity;
    private int legsFrame = -10;
    private bool legsAsked;
    private float layYaw;
    // The frame the cycles last played on (a landing or a jump plays none), and the numbers behind their pace for the /gait
    // readout.
    private int strideFrame = -1;
    private float gaitSpeed, gaitNatural, gaitRateNow = 1f;
    // How much of the body was on its cycles this frame rather than standing, for the run's calm camera (PlayerBody.Look).
    private float runCalmShare;


    private void AddLocomotionChannels()
    {
        legLength = LegOf(animator);
        plainSet = SetFromTable(PlainSetId, idle) ?? new MoveSet();
        crouchSet = SetFromTable(CrouchSetId, crouchIdle) ?? new MoveSet();
        gunSets.Clear();
        kindOfAmmunition.Clear();
        foreach (string id in GameTables.RowIds(MovesTable))
        {
            if (id.IndexOf('.') >= 0 || id.StartsWith("#", System.StringComparison.Ordinal)
                || string.Equals(id, PlainSetId, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, CrouchSetId, System.StringComparison.OrdinalIgnoreCase)) continue;
            gunSets[id] = new GunSets
            {
                Hip = SetFromTable(id, null), Aim = SetFromTable(id + ".aim", null),
                Crouch = SetFromTable(id + ".crouch", null), CrouchAim = SetFromTable(id + ".crouch.aim", null)
            };
            if (GameTables.TryGetRow(MovesTable, id, out var row))
                foreach (string ammunition in row.Text("ammo").Split(new[] { ';', ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
                    kindOfAmmunition[ammunition] = id;
        }
        // The forward cycles' leaning and arcing ones, banked into by the view's turn (PlayerBody.Leans).
        AddLeans();
        turning = 0;
        strideCount = 0;
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

    // A set as its table row has it: its idle (the row's, else the stance's own), and its cycles grouped by the way each
    // one's feet were measured to carry this body. Null for a row that isn't there or holds nothing this body can play.
    private MoveSet SetFromTable(string id, string stanceIdle)
    {
        if (!GameTables.TryGetRow(MovesTable, id, out var row)) return null;
        var set = new MoveSet();
        string still = row.Text("idle");
        if (string.IsNullOrEmpty(still)) still = stanceIdle;
        if (!string.IsNullOrEmpty(still))
        {
            AddChannel(still, 0f);
            byName.TryGetValue(still, out set.Idle);
        }
        foreach (string clip in row.Text("moves").Split(new[] { ';', ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            var cycle = AddCycle(clip);
            if (cycle == null) continue;
            if (cycle.Gait == null || !cycle.Gait.Moves)
            {
                Report($"'{clip}' was listed as a walk or run but its feet don't carry the body (GaitClips): left out.");
                continue;
            }
            float angle = Mathf.Repeat(Mathf.Atan2(cycle.Gait.Way.x, cycle.Gait.Way.y) * Mathf.Rad2Deg, 360f);
            Way way = null;
            foreach (var known in set.Ways)
                if (Mathf.Abs(Mathf.DeltaAngle(known.Angle, angle)) <= SameWay) { way = known; break; }
            if (way == null)
            {
                way = new Way { Angle = angle };
                set.Ways.Add(way);
            }
            if (!way.Cycles.Contains(cycle)) way.Cycles.Add(cycle);
        }
        foreach (var way in set.Ways)
        {
            way.Cycles.Sort((a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : a.Gait.Speed.CompareTo(b.Gait.Speed));
            // The way is where its cycles go together: the mean of their measured ways.
            Vector2 sum = Vector2.zero;
            foreach (var cycle in way.Cycles) sum += cycle.Gait.Way;
            if (sum.sqrMagnitude > 1e-6f) way.Angle = Mathf.Repeat(Mathf.Atan2(sum.x, sum.y) * Mathf.Rad2Deg, 360f);
        }
        set.Ways.Sort((a, b) => a.Angle.CompareTo(b.Angle));
        return set.Idle == null && set.Ways.Count == 0 ? null : set;
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
        if (channel.Rank < 0f) channel.Rank = RankOf(clipName, channel);
        return channel;
    }

    // Which gait a cycle is, by the word its name has (BIPER names every cycle so: Walk_Lfoot, StrafeJog_FL,
    // R_AimJog, CrouchWalk_F; a cycle polished in Blender keeps its name after "enf_"). A run is the sprint: the game has
    // had no run gait since 0.9.283, and a gun's run (Combat Girls R_Run, P_Run, S_Run) is how a body holding that gun
    // sprints (0.9.314). A cycle named otherwise (a mod's) takes the gait whose speed its feet are nearest, straight ahead.
    private float RankOf(string clipName, Channel cycle)
    {
        string name = clipName ?? "";
        if (name.IndexOf("Sprint", System.StringComparison.OrdinalIgnoreCase) >= 0) return 2f;
        if (name.IndexOf("Run", System.StringComparison.OrdinalIgnoreCase) >= 0) return 2f;
        if (name.IndexOf("Jog", System.StringComparison.OrdinalIgnoreCase) >= 0) return 1f;
        if (name.IndexOf("Walk", System.StringComparison.OrdinalIgnoreCase) >= 0) return 0f;
        var gait = player != null && player.Actions != null ? player.Actions.Gait : null;
        return gait != null && cycle.Gait != null ? Mathf.Round(gait.PlaceOf(cycle.Gait.Speed * GaitScale, 0f)) : 0f;
    }

    /// <summary>A gun out has the legs: its own sets (BodyMoves.csv).</summary>
    internal bool GunHasLegs => GunInHand() != null;
    /// <summary>How far the legs are on its aim walk (0-1).</summary>
    internal float AimShare => GunHasLegs ? AimBlend() : 0f;

    // The gun kind's sets in the hands now, by the ammunition the gun takes (BodyMoves.csv ammo; "*" for the rest), or
    // null with no gun out. A friend's copy has the same gun out, so the same sets.
    private GunSets GunInHand()
    {
        if (weapons == null) weapons = player.GetComponent<WeaponController>();
        var gun = weapons != null && weapons.ShowsWeapon ? weapons.EquippedData : null;
        if (gun == null || gunSets.Count == 0) return null;
        if (!kindOfAmmunition.TryGetValue(gun.Ammunition, out string kind) && !kindOfAmmunition.TryGetValue(AnyAmmunition, out kind))
            return null;
        return gunSets.TryGetValue(kind, out var sets) ? sets : null;
    }

    // The cycles are measured in the Animator's own units; its scale (the fit to the camera) makes them metres.
    private float GaitScale => animator != null ? Mathf.Abs(animator.transform.lossyScale.y) : 1f;

    private void Locomotion(bool crouched, float dt)
    {
        // The player's own legs go as the player asks (PlayerMovement.Stride: the keys' way at the gait's speed, eased), and the
        // body goes as they carry it (LegsCarry). Where something else moves the body this frame (a surge, a hold) and another
        // PC's always, they go as the body is seen to go, followed in the world so turning the view doesn't drag them behind it.
        // 0.9.415 played the player's own on the last stride asked whatever moved the body, and a held body sprinted on the
        // spot (the review of 2026-11-25).
        legsAsked = !player.IsNetworkDriven && player.StrideFrame == Time.frameCount;
        if (legsAsked) stride = Vector3.ProjectOnPlane(player.Stride, Vector3.up);
        else stride = FollowStride(stride, Vector3.ProjectOnPlane(player.ActualVelocity, Vector3.up),
            strideFollowTime > 0f ? 1f - Mathf.Exp(-dt / strideFollowTime) : 1f);
        Vector3 local = transform.InverseTransformDirection(stride);
        local.y = 0f;
        float speed = local.magnitude;
        // Still or moving is one flag, with a gap between its two speeds, and the heading is the travel's whenever it has one,
        // kept as the body slows to a stop: the moving share fades it. Until 0.9.400 one speed (StillTravel) switched the
        // heading to straight ahead, the place keeping, the turn on the spot and the cycles' shares all at once, so a backward or
        // sideways start or stop swapped the legs to the forward cycle in a frame, and a speed hovering there flipped all five
        // every frame (the audit of 2026-11-24).
        // It stands once it has been slower than StillEnter for StillAfter: a reversal (A then D, W then S) goes through no speed
        // for a frame or two and is no standing. Standing that frame, the feet were kept where they were (FootPlanting.Stand),
        // and as the body went on the other way a kept foot was left up to 70 cm off where its clip had it and swung back
        // across when it lifted, 67 times in one play (the feet record of 2026-11-25 07:11, fixed in 0.9.428 and lost with
        // the rollback to 0.9.424; the experts' review of 2026-11-26).
        // Keys swept round faster than a stride can step stand the legs where they are, the body going as asked over them
        // (Dances).
        dancing = Dances(stride, speed);
        if (dancing) still = true;
        else if (still) still = speed < StillLeave;
        else
        {
            slowFor = speed < StillEnter ? slowFor + dt : 0f;
            still = slowFor >= StillAfter;
        }
        if (still) slowFor = 0f;
        // What the legs walk or run at: nothing while they dance.
        float pace = dancing ? 0f : speed;
        if (speed > HeadingLeast) travelHeading = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        float heading = travelHeading;
        keepsPlace = still;
        float scale = GaitScale;
        mixWeight = mixStride = 0f;
        mixLength = Vector2.zero;
        postureClip = PostureClip(crouched);
        FollowTurn(dt);

        // A gun out asks for its aimed set as far as the sights are up and for its own set otherwise; what they can't
        // carry the stance's own set (plain or crouched) does.
        var gun = GunInHand();
        float aim = gun != null ? AimBlend() : 0f;
        bool sprinting = SprintingNow();
        bool gunRan = GunRuns;
        gunRuns = !crouched && gun != null && sprinting && !dancing;
        gunRunsFrame = Time.frameCount;
        // The gun's run takes the whole body at once, or gives it back: that is a switch within the legs' mix (Blend).
        if (gunRuns != gunRan && mixedFrame == Time.frameCount - 1) bodyInertia.Begin(blendTime);
        // The gun's run goes ahead only: at a slant ahead the hips turn to the way it runs and the run plays, the spine turning
        // the chest back to the view (SpineLook). The user, 2026-11-22: "WA, WD까진 허용할 수 있도록 한다. 자연스럽게".
        float turn = gunRuns ? Mathf.Clamp(heading, -GunRunSlant, GunRunSlant) : 0f;
        // Running ahead the hips are laid first (below), so the legs are laid under where the body faces this frame and it goes
        // the way they carry it (Pace): under last frame's, a sprint's arc turned 2.5 degrees a frame beneath them and their feet
        // slid aside (the review of 2026-11-25). Otherwise the facing follows the view a moment behind (FacingLagMost).
        bool ahead = !crouched && !legsPlanted && player.RunsAhead && !still;
        layYaw = yaw;
        if (ahead)
        {
            // With the run once they face its way, as it curves; coming round to it at HipsCatch times its arc's rate (a strafe
            // pressed on ahead), so they never swing round in a frame. Followed at the view's pace instead, they trailed a
            // sprint's arc by a fifth of a turn and the legs mixed in the strafe beside it.
            float view = transform.eulerAngles.y;
            layYaw = Mathf.DeltaAngle(view, Mathf.MoveTowardsAngle(legYaw, view + heading, ArcRate * HipsCatch * dt));
        }
        // The way the legs go against the way the body faces.
        float legs = Mathf.DeltaAngle(0f, heading - layYaw), moving = 0f;
        var gaitNow = player.Actions != null ? player.Actions.Gait : null;
        // Down into a crouch or up out of it on the move, the two stances share the legs as far as the body is down
        // (PlayerBody.Posture).
        if (postureDown < 1f)
        {
            asking.Clear();
            wanting.Clear();
            if (gun != null) { Ask(gun.Aim, aim); Ask(gun.Hip, 1f); }
            // The gait's own cycles play (MovementGait.Position), fitted to the speed by the stride (Pace), never a gait faster
            // than the speed reached yet; aimed, they walk. Until 0.9.282 the cycles were picked by speed alone, against what
            // their feet carry on this body: our bodies' feet carry 20-35% less than the clips' own travel, so each gait played
            // half of the next one's clip (the jog half the run, the run a quarter of the sprint), and the run and the sprint
            // looked alike (the user, 2026-11-21: "달리기랑 전력 질주랑 걍 모션 똑같은 거 같은데").
            gaitPlace = gaitNow == null ? 0f : Mathf.Min(gaitNow.Position, gaitNow.PlaceOf(pace, legs)) * (1f - aim);
            moving += MixSets(plainSet, legs, pace, scale, 1f - postureDown);
            Bank();
        }
        if (postureDown > 0f)
        {
            asking.Clear();
            wanting.Clear();
            if (gun != null) { Ask(gun.CrouchAim, aim); Ask(gun.Crouch, 1f); }
            // Crouched, the crouched walk.
            gaitPlace = 0f;
            moving += MixSets(crouchSet, legs, pace, scale, postureDown);
        }
        postureClip = null;
        // Crouched too since 0.9.279: the legs keep their place until the view has gone far enough, and the feet step.
        TurnOnTheSpot(still, dt);
        // Running ahead (PlayerMovement.RunsAhead: a jog or a sprint asked within a slant of straight on, standing and not aimed)
        // the hips face the way the body runs, which curves no faster than the run's own arc clip (LocomotionState), and the
        // spine and neck turn the head to the view (SpineLook); further round than the run comes about is a turn back (RunTurn).
        // A run let go runs ahead still, so it coasts to a stop facing its own way and turns to the view on its feet after
        // (TurnOnTheSpot). Otherwise the hips face the view and the legs go the way the keys ask beneath them, the gun's run
        // turned to a slant ahead. Until 0.9.416 the hips always followed the view: a view flicked on the run had the legs sprint
        // sideways and backwards (StrafeSprint_R135, _B) under a body that swung up to 65 degrees a frame to keep within 90 of
        // it (the user, 2026-11-24: "모션이 전반적으로 … 우스꽝스러워졌는데"), and 0.9.411 faced a body coasting to a stop its
        // own way only when it was within 30 degrees of it.
        if (ahead)
        {
            targetYaw = layYaw;
            legsPlanted = rootFollowed = true;
        }
        else if (!crouched && !legsPlanted) targetYaw = turn;
        if (!still) walkedFrame = Time.frameCount;
        mixedFrame = Time.frameCount;
        Pace(pace, legs, moving, dt);
    }

    // Keys swept round faster than a stride can step: a diamond (a key every 0.12 s) turns the way the legs are asked to go
    // about 300 degrees in DanceWindow, A and D in turn every 0.2 s 360. No walk or run cycle can step that, and as the eight
    // ways' cycles were mixed round with the way the place a foot down had in them went round too: the feet down slid over the
    // ground at 3 m/s or more for half of such a dance, and the legs flailed (the move test of 2026-11-26: diamond slide
    // median 3.2 m/s, 159 frames over 3 m/s in 4 s; A and D every 0.2 s, 94; the user: "극단적인 다이아몬드 스텝에서 문제가
    // 발생하면 어떻게 하면 고칠 수 있을까", "전부 다 고쳐라"). So the legs stand while the way turns DanceTurn degrees or
    // more in DanceWindow, until it turns no more than DanceSettled: the feet stay where they are down and the hips go round
    // over them, the body going as asked and not as the legs carry it (Pace), so the keys answer at once as ever; a foot the
    // body leaves out of its reach steps after it (FootPlanting.Stand). A single reversal turns the way 180 degrees, a
    // diamond of a key every 0.25 s about 145, and a run's own curve no more than its arc's 60: those still step. The way is
    // read only above StillLeave: at a crawl, through the stop of a reversal, it says little.
    private const float DanceWindow = .4f, DanceTurn = 270f, DanceSettled = 180f;
    private bool dancing;

    /// <summary>The legs stand while the keys sweep round faster than a stride can step (Dances; MoveTest counts it).</summary>
    internal bool Dancing => dancing;
    // The way's turns within the last DanceWindow (time, degrees), oldest first, their sum, and the way last frame.
    private readonly Queue<Vector2> danceTurns = new Queue<Vector2>();
    private float danceTurned, danceWay;
    private bool danceWayKnown;

    private bool Dances(Vector3 stride, float speed)
    {
        // Taken up afresh as the legs come back to walking from anything else (a vault, a slide, the air): the way they went
        // before it is no turn of the keys, and a dance from before it is over.
        if (mixedFrame != Time.frameCount - 1)
        {
            danceTurns.Clear();
            danceTurned = 0f;
            danceWayKnown = dancing = false;
        }
        float now = Time.time;
        if (speed > StillLeave)
        {
            float way = Mathf.Atan2(stride.x, stride.z) * Mathf.Rad2Deg;
            if (danceWayKnown)
            {
                float turn = Mathf.Abs(Mathf.DeltaAngle(danceWay, way));
                if (turn > 0f)
                {
                    danceTurns.Enqueue(new Vector2(now, turn));
                    danceTurned += turn;
                }
            }
            danceWay = way;
            danceWayKnown = true;
        }
        while (danceTurns.Count > 0 && now - danceTurns.Peek().x > DanceWindow) danceTurned -= danceTurns.Dequeue().y;
        if (danceTurns.Count == 0) danceTurned = 0f;
        return dancing ? danceTurned > DanceSettled : danceTurned >= DanceTurn;
    }

    // Slower than StillEnter the body stands, faster than StillLeave it moves (m/s); its travel has a heading faster than
    // HeadingLeast. StillTravel is a curve's least speed (Leans).
    private const float StillTravel = .3f, StillEnter = .25f, StillLeave = .35f, HeadingLeast = .05f, StillAfter = .15f;
    private bool still = true;
    // How long the body has been slower than StillEnter while moving (s).
    private float slowFor;
    // The frame the legs' own mix last ran (Locomotion), and whether it ran the frame before: within it the clips' shares follow
    // the speed and the way as they go, which is no switch to ease over (Blend).
    private int mixedFrame = -10;
    private bool mixedBefore;
    // The frame the legs last walked or ran (Locomotion, not standing) or a run's turn had them (RunTurnPose): the body sits on
    // its hips (SitOnHips).
    private int walkedFrame = -10;

    /// <summary>How fast a run may curve at the legs' gait now (degrees a second): its arc clip's own turn (BodyLeans.csv
    /// arcRate), between two gaits by the place between them; the fastest of a gait's where it has more (the sprint's over the
    /// run's). A body with no arcs turns as fast as it likes.</summary>
    internal float ArcRate
    {
        get
        {
            float belowRank = float.NegativeInfinity, aboveRank = float.PositiveInfinity, below = 0f, above = 0f;
            foreach (var lean in leans)
            {
                float rank = lean.Straight.Rank;
                if (rank <= gaitPlace && (rank > belowRank || rank == belowRank && lean.ArcRate > below)) { belowRank = rank; below = lean.ArcRate; }
                if (rank >= gaitPlace && (rank < aboveRank || rank == aboveRank && lean.ArcRate > above)) { aboveRank = rank; above = lean.ArcRate; }
            }
            if (below <= 0f) return above > 0f ? above : FreeArc;
            if (above <= 0f || aboveRank <= belowRank) return below;
            return Mathf.Lerp(below, above, (gaitPlace - belowRank) / (aboveRank - belowRank));
        }
    }

    // A body with no arc clips curves this fast at most (degrees a second). Hips coming round to a run ahead turn this many
    // times its arc's rate, so they catch up with a run that curves at it.
    private const float FreeArc = 720f, HipsCatch = 1.5f;
    private float travelHeading;

    // The stride follows the travel's way and its speed each on their own, the way turning round at the same speed. Followed
    // as one, a way that swung round fast went through short in between: running on at 4-5 m/s through a sharp turn the legs
    // read 2 and played the walk's clips (걸음 기록 of 2026-11-24, a sprint's flick of the view back: StrafeWalk under a body
    // going 5 m/s for a fifth of a second).
    private static Vector3 FollowStride(Vector3 stride, Vector3 travel, float follow)
    {
        float speed = Mathf.Lerp(stride.magnitude, travel.magnitude, follow);
        if (travel.sqrMagnitude < 1e-6f) return stride.sqrMagnitude > 1e-6f ? stride.normalized * speed : Vector3.zero;
        if (stride.sqrMagnitude < 1e-6f) return travel.normalized * speed;
        float turn = Vector3.SignedAngle(stride, travel, Vector3.up);
        return Quaternion.AngleAxis(turn * follow, Vector3.up) * stride.normalized * speed;
    }

    // This frame's gait for the cycles (0 walk, 1 jog, 2 sprint): the gait gone, no faster than the speed reached.
    private float gaitPlace;

    // How long a whole stride of both feet takes now at the pace the cycles play (s; 0 with no cycle playing): the feet time
    // their swings by it (FootPlanting.Steps).
    private float strideCycle;

    // Standing still this frame (Locomotion): the body keeps its place on the ground (PlaceBody, FollowHead).
    private bool keepsPlace;

    private void Ask(MoveSet set, float want)
    {
        if (set == null || want <= 0f) return;
        asking.Add(set);
        wanting.Add(want);
    }

    // Shares the legs among the sets asking for them, first asked first: each takes what it wants of what is left, as far
    // as its cycles can carry this travel (Carries); the stance's own set has the rest. Before 0.9.240 a gun's set was its
    // aim cycles alone, taken only while aiming (or by Body.gunHipMoves), and a set missing any of four ways fell back
    // whole to the plain one. How much of the body moves rather than stands comes back, of the part of it they have.
    private float MixSets(MoveSet stance, float heading, float speed, float scale, float part)
    {
        float left = part, moving = 0f;
        for (int i = 0; i < asking.Count && left > 1e-4f; i++)
        {
            float share = left * Mathf.Clamp01(wanting[i]) * (still ? 1f : Carries(asking[i], heading, gaitPlace));
            if (share <= 0f) continue;
            moving += Mix(asking[i], stance, share, heading, speed, scale);
            left -= share;
        }
        if (left > 1e-4f) moving += Mix(stance, stance, left, heading, speed, scale);
        return moving;
    }

    /// <summary>Sprinting with a gun out now: the gun's run (BIPER Combat Girls R_Run, P_Run, S_Run) has the whole body, arms
    /// and gun with it (ahead only: SprintsToward): the hands give the upper body to the clip (PlayerBodyHands), the gun rides the clip's right hand (FPSBody) and the sights and the trigger wait
    /// (WeaponController). The user, 2026-11-22: "전력질주할 때 줌이나 총 못 쏘게 하고 combat girls animation을 처쓰라고".
    /// False out of a walk or run on the ground (Locomotion not played this frame).</summary>
    public bool GunRuns => gunRuns && Time.frameCount - gunRunsFrame <= 2;

    /// <summary>How far the sights are up on the gun's own aim clips (0-1, FollowAim): standing or crouched
    /// with a gun out, the aim clips (BIPER Combat Girls *_AimIdle, *_AimWalk_*, *_AimJog,
    /// BodyMoves .aim sets) have the upper body and arms, the hands give it to them (PlayerBodyHands), the gun is sighted
    /// on the camera (FPSBody.KeepGunInView) and the camera is on the right eye (Look). Before 0.9.318 FPSAF had the upper
    /// body and the camera sat between the eyes, and the gun went through the head between the brows as others saw it (the user, 2026-11-22: "조준하는 것도 이 애니메이션 써. 지금 제 3자가
    /// 보기에 내 미간 사이를 뚫어서 총을 위치하게 해서 조준하잖아, 카메라 때문이지만").</summary>
    public float AimsOnClip => aimShown;

    // The sights as shown on the aim clips: up as the aim comes up, down no faster than the gun's own aim time. A wall, a
    // shove, a step dodge or the pistol's close hold ends the aim at once, and the gun fell from the sight to the hip in a
    // frame, the camera 3 cm with it (0.9.318-0.9.320).
    private float aimShown;

    // In Update with the look (AimLook), once a frame.
    private void FollowAim(float dt)
    {
        bool stance = state == PlayerMovement.MovementState.Standing || state == PlayerMovement.MovementState.Crouching;
        float now = !dead && stance && !GunRuns && GunInHand() != null ? AimBlend() : 0f;
        if (dead || now >= aimShown) { aimShown = now; return; }
        float time = weapons != null ? weapons.AdsTime : .2f;
        aimShown = Mathf.MoveTowards(aimShown, now, dt / Mathf.Max(.01f, time));
    }

    /// <summary>Whether the body has a sprint toward these keys (x right, y ahead, against the view): with a gun out only
    /// ahead and at a slant ahead (W, W+A, W+D: within GunRunSlant of a way the gun's own run goes, which the hips turn to,
    /// Locomotion); with none every way (the 8-way sprints). The user, 2026-11-22: "총 들었을 때 모션 없으면 앞만 달릴 수
    /// 있게 해" (0.9.316 turned the body to run the gun's run backwards: "뒤로 달리니까 개 어색하니까"), then "WA, WD까진
    /// 허용할 수 있도록 한다. 자연스럽게".</summary>
    public bool SprintsToward(Vector2 keys)
    {
        var gun = GunInHand();
        if (gun == null || gun.Hip == null || keys.sqrMagnitude < 1e-4f) return true;
        float heading = Mathf.Atan2(keys.x, keys.y) * Mathf.Rad2Deg;
        foreach (var way in gun.Hip.Ways)
            if (way.Cycles.Count > 0 && way.Cycles[way.Cycles.Count - 1].Rank >= 2f
                && Mathf.Abs(Mathf.DeltaAngle(way.Angle, heading)) <= GunRunSlant + GunRunSlantSlack)
                return true;
        return false;
    }

    // How far round from the gun's run the gun sprints, the hips turned to it (degrees): a diagonal key's 45. The run's
    // way is measured from its feet, a degree or two off straight ahead (R_Run's -1°, the set's mean 1.5°), so a diagonal
    // key is let in that much further: at 1° it shut W+A out (0.9.319; the user: "W+A는 안 되잖아").
    private const float GunRunSlant = 45f, GunRunSlantSlack = 10f;

    // The player is sprinting now: gunRunFrom of the way into the sprint and not letting go. The sprint's progress only
    // falls once Shift is let go, so the gun comes back at once; a friend's copy gets it in network steps, so an unchanged
    // value keeps what it was.
    private bool SprintingNow()
    {
        float sprint = player.SprintProgress;
        if (sprint > sprintSeen + 1e-4f) sprintRising = true;
        else if (sprint < sprintSeen - 1e-4f) sprintRising = false;
        sprintSeen = sprint;
        return sprint >= gunRunFrom && (sprintRising || sprint >= .999f);
    }

    // A set's share: on its idle as far as the body stands (the stance's idle for a set with none of its own), on its
    // cycles for the rest. Returns the moving part of the share.
    private float Mix(MoveSet set, MoveSet stance, float share, float heading, float speed, float scale)
    {
        float moving = share * MovingShare(set, heading, speed, scale);
        MixIdle(set.Idle != null ? set : stance, share - moving);
        Ways(set, heading, speed, moving, scale);
        return moving;
    }

    // The part of the body that stands: on its set's idle, or on the clip that takes it down into a crouch or up out of one.
    private void MixIdle(MoveSet set, float weight)
    {
        var idle = postureClip ?? set.Idle;
        if (idle != null && weight > 0f) idle.Target += weight;
    }

    // How much of a travel a set can carry at a gait (0-1): as far as its ways reach round to the heading (Reach) and its
    // cycles reach the gait (a gun held at the hip walks, jogs and runs ahead only: the plain set steps the other ways under
    // the gun's upper body, and a gun's sprint goes ahead only, SprintsToward). Past that the next set down runs on under the
    // same upper body.
    private static float Carries(MoveSet set, float heading, float place)
    {
        if (!Pair(set, heading, out var a, out var b, out float toward)) return 0f;
        float top = Mathf.Lerp(a.Cycles[a.Cycles.Count - 1].Rank, b.Cycles[b.Cycles.Count - 1].Rank, toward);
        return Reach(set, heading) * (1f - Mathf.InverseLerp(top, top + .5f, place));
    }

    /// <summary>How fast the legs' own clips go this way (m/s; degrees from ahead, + to the right): at this gait (0 walk, 1 jog,
    /// 2 sprint) or crouched, and this far aimed (0-1). The sets share it as they share the legs (MixSets), each way's clips
    /// at the speed their feet carry this body (GaitClips). The legs are asked for it (MovementGait), so whatever clips a set
    /// lists, the game goes as they do. 0 for a body with none. Until 0.9.415 it was their root's speed
    /// (ClipMotion.CycleSpeed x Body.clipMetres), which no foot of ours goes at: the jog's 2.74 m/s against its feet's 2.52.</summary>
    internal float LegSpeed(float heading, float place, bool crouched, float aim)
    {
        var stance = crouched ? crouchSet : plainSet;
        if (stance == null) return 0f;
        var gun = GunInHand();
        if (gun == null) aim = 0f;
        place = crouched ? 0f : place * (1f - aim);
        // The hips turn to the way the gun's run goes (Locomotion).
        if (gun != null && place >= 1f + gunRunFrom) heading -= Mathf.Clamp(heading, -GunRunSlant, GunRunSlant);
        float left = 1f, speed = 0f, scale = GaitScale;
        if (gun != null)
        {
            Take(crouched ? gun.CrouchAim : gun.Aim, aim, ref left, ref speed);
            Take(crouched ? gun.Crouch : gun.Hip, 1f, ref left, ref speed);
        }
        return speed + left * OwnSpeed(stance, heading, place, scale);

        void Take(MoveSet set, float want, ref float left, ref float speed)
        {
            if (set == null || want <= 0f) return;
            float share = left * Mathf.Clamp01(want) * Carries(set, heading, place);
            speed += share * OwnSpeed(set, heading, place, scale);
            left -= share;
        }
    }

    /// <summary>The fastest the legs go ahead (m/s): the empty hands' sprint.</summary>
    private float SprintOwn => plainSet != null ? OwnSpeed(plainSet, 0f, 2f, GaitScale) : 0f;

    // A set's own speed this way at this gait (m/s): its two ways' either side, joined by the angle, each as fast as its cycles'
    // feet carry this body (the Animator's units to metres by its scale).
    private static float OwnSpeed(MoveSet set, float heading, float place, float scale)
    {
        if (!Pair(set, heading, out var a, out var b, out float toward)) return 0f;
        return Mathf.Lerp(WayOwn(a), WayOwn(b), toward);

        float WayOwn(Way way)
        {
            Bracket(way.Cycles, place, out int low, out int high, out float t);
            return Mathf.Lerp(way.Cycles[low].Gait.Speed, way.Cycles[high].Gait.Speed, t) * scale;
        }
    }

    // How much of the body moves rather than stands: up to the slowest cycle the way it goes, the stance shares it.
    private float MovingShare(MoveSet set, float heading, float speed, float scale)
    {
        if (!Pair(set, heading, out var a, out var b, out float toward)) return 0f;
        float slow = Mathf.Lerp(a.Cycles[0].Gait.Speed, b.Cycles[0].Gait.Speed, toward) * scale;
        return Mathf.Clamp01(speed / Mathf.Max(.1f, slow));
    }

    // The two ways of a set either side of a heading (0 ahead, 90 right), round the circle: how far the heading is from the
    // first toward the second (0-1) and how far apart they are (degrees). A set of one way has it as both.
    private static bool Around(MoveSet set, float heading, out Way from, out Way to, out float toward, out float gap)
    {
        from = to = null;
        toward = 0f;
        gap = 360f;
        int count = set.Ways.Count;
        if (count == 0) return false;
        from = to = set.Ways[0];
        if (count == 1) return true;
        float at = Mathf.Repeat(heading, 360f);
        for (int i = 0; i < count; i++)
        {
            Way a = set.Ways[i], b = set.Ways[(i + 1) % count];
            float span = Mathf.Repeat(b.Angle - a.Angle, 360f), off = Mathf.Repeat(at - a.Angle, 360f);
            if (off > span) continue;
            from = a;
            to = b;
            gap = span;
            toward = span > 1e-3f ? off / span : 0f;
            return true;
        }
        return true;
    }

    // The ways a heading blends: the two either side while they join (WaysJoin), else the nearer one alone.
    private static bool Pair(MoveSet set, float heading, out Way from, out Way to, out float toward)
    {
        if (!Around(set, heading, out from, out to, out toward, out float gap)) return false;
        if (from != to && gap > WaysJoin)
        {
            if (toward > .5f) from = to;
            else to = from;
            toward = 0f;
        }
        return true;
    }

    // How far a set's ways reach round to a heading (0-1): all the way between two that join; within LoneWayReach of a way
    // with no joining neighbour on that side, none from LoneWayEnd.
    private static float Reach(MoveSet set, float heading)
    {
        if (!Around(set, heading, out var a, out var b, out float toward, out float gap)) return 0f;
        if (a != b && gap <= WaysJoin) return 1f;
        float off = a == b ? Mathf.Abs(Mathf.DeltaAngle(a.Angle, heading)) : Mathf.Min(toward, 1f - toward) * gap;
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LoneWayReach, LoneWayEnd, off));
    }

    // Shares the weight between the two ways either side of the heading so their blended feet push back along it: each
    // way's cycles carry the body their own way as far as a stride takes them, and the share is the one whose sum of the two
    // points down the heading. The cycles share one step (Blend), so the blended feet go the summed stride lengths in the
    // summed stride times; summed speeds point elsewhere where the two step at different paces (the jog's 0.93 s stride
    // against a strafe's 0.8), and until 0.9.415 they were what was summed. Shared by angle alone, a fast forward run and a
    // slow side step blended into a stride that pointed well off the travel, and the feet slid sideways.
    private void Ways(MoveSet set, float heading, float speed, float weight, float scale)
    {
        if (weight <= 0f || !Pair(set, heading, out var a, out var b, out float toward)) return;
        if (a == b)
        {
            MixLadder(a.Cycles, speed, weight, scale);
            return;
        }
        float share = toward;
        if (toward > 1e-4f && toward < 1f - 1e-4f)
        {
            float rad = heading * Mathf.Deg2Rad;
            var way = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            Vector2 first = LadderLength(a.Cycles, gaitPlace, scale), second = LadderLength(b.Cycles, gaitPlace, scale);
            float x = Cross(first, way), y = Cross(second, way);
            if (Mathf.Abs(x - y) > 1e-4f) share = Mathf.Clamp01(x / (x - y));
        }
        MixLadder(a.Cycles, speed, weight * (1f - share), scale);
        MixLadder(b.Cycles, speed, weight * share, scale);

        static float Cross(Vector2 p, Vector2 q) => p.x * q.y - p.y * q.x;
    }

    // The two cycles either side of this frame's gait (gaitPlace) by their gaits; below the slowest or above the fastest,
    // that one.
    // Named MixLadder, not Ladder: inside PlayerBody a member called Ladder hides the Ladder fixture type (PlayerBody.LadderFeet).
    private void MixLadder(List<Channel> cycles, float speed, float weight, float scale)
    {
        if (weight <= 0f || cycles.Count == 0) return;
        Bracket(cycles, gaitPlace, out int low, out int high, out float t);
        MixCycle(cycles[low], weight * (1f - t), scale);
        if (high != low) MixCycle(cycles[high], weight * t, scale);
    }

    private static Vector2 LadderLength(List<Channel> cycles, float place, float scale)
    {
        Bracket(cycles, place, out int low, out int high, out float t);
        return Length(cycles[low], scale) * (1f - t) + Length(cycles[high], scale) * t;
    }

    private static void Bracket(List<Channel> cycles, float place, out int low, out int high, out float t)
    {
        low = high = 0;
        t = 0f;
        int last = cycles.Count - 1;
        if (last <= 0 || place <= cycles[0].Rank) return;
        if (place >= cycles[last].Rank) { low = high = last; return; }
        for (int i = 0; i < last; i++)
        {
            float a = cycles[i].Rank, b = cycles[i + 1].Rank;
            if (place > b) continue;
            low = i;
            high = i + 1;
            t = b - a > 1e-3f ? (place - a) / (b - a) : 1f;
            return;
        }
    }

    private static Vector2 Velocity(Channel cycle, float scale) => cycle.Gait.Way * (cycle.Gait.Speed * scale);
    // How far a cycle's feet carry the body in a stride of both feet (m, the body's frame).
    private static Vector2 Length(Channel cycle, float scale) => Velocity(cycle, scale) * cycle.Gait.Stride;

    private void MixCycle(Channel cycle, float weight, float scale)
    {
        if (weight <= 0f) return;
        cycle.Target += weight;
        mixWeight += weight;
        mixStride += weight * cycle.Gait.Stride;
        mixLength += weight * Length(cycle, scale);
    }

    // The step's pace and what it carries. The cycles share one step (Blend), so their feet go the summed stride lengths in the
    // summed stride times. The player's own play at their own pace and the body goes as they carry it (LegsCarry), the idle's
    // share standing; faster only for what the gait's speed is put above the fastest of them (a class's pace), within
    // gaitRate. Another PC's play at the pace that carries its body as the network has it, within gaitRate. Until 0.9.415 every
    // body was carried by the keys and its cycles' pace made up the difference; until 0.9.410 the stride was also stretched to
    // the speed (Body.gaitStretch), which, coming on whole as a run took over from a turn's clip, threw the swinging foot 15 to
    // 25 cm in a frame (the user, 2026-11-24: "보폭 늘리기라는 시스템 때문에 지금 이랬다고? 그럼 없애면 되잖아").
    private void Pace(float speed, float legs, float moving, float dt)
    {
        // Dancing on the spot the legs carry nothing: the body goes as asked (Dances).
        bool carries = legsAsked && !dancing;
        if (player.IsNetworkDriven) FollowStep(dt);
        gaitSpeed = speed;
        runCalmShare = moving;
        legsVelocity = Vector3.zero;
        // A body with no cycles the way it is asked (none measured) carries nothing: it goes as asked (LocomotionState). 0.9.415
        // said it carried nothing at any speed, and such a body stood (the review of 2026-11-25).
        if (carries && (mixWeight > 0f || speed < StillEnter)) legsFrame = Time.frameCount;
        if (mixWeight <= 0f)
        {
            gaitNatural = 0f;
            gaitRateNow = 1f;
            strideCycle = 0f;
            return;
        }
        float strideTime = Mathf.Max(.05f, mixStride / mixWeight);
        // What the cycles' feet carry the body at their own pace (m/s, the body's frame).
        Vector2 own = mixLength / Mathf.Max(1e-4f, mixStride);
        float rad = legs * Mathf.Deg2Rad;
        float along = Vector2.Dot(own, new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)));
        float rate = 1f, fastest = Mathf.Max(gaitRate.x, gaitRate.y);
        if (along > .05f && speed > .05f) rate = Mathf.Clamp(speed / along, carries ? 1f : Mathf.Min(gaitRate.x, gaitRate.y), fastest);
        // Into the world as the legs were laid out (the heading against the body's facing, Locomotion).
        // The lean and arc cycles banked in after (Bank) step with their straight one, a run curving under hips that turn with
        // it, at its pace within a fiftieth; their own measured ways are the chord of their turn, not where they carry the hips.
        if (carries) legsVelocity = Quaternion.Euler(0f, transform.eulerAngles.y + layYaw, 0f) * new Vector3(own.x, 0f, own.y) * (mixWeight * rate);
        // Setting off, from standing or through the stop of a reversal, the stride waits where the feet pass each other
        // (SetOffPhase). Taken up from wherever it last stopped, a start brought the stride in whole in a tenth of a second
        // with both feet on the ground and apart, and they slid 40-50 cm across it (the feet record of 2026-11-25 08:49; found
        // in 0.9.434 and lost with the rollback to 0.9.424; the experts' review of 2026-11-26).
        if (moving < SetOffBelow) phase = SetOffPhase(legs);
        phase += dt * rate / strideTime;
        strideCycle = strideTime / Mathf.Max(.05f, rate);
        if (phase >= 1f)
        {
            strideCount += Mathf.FloorToInt(phase);
            phase -= Mathf.Floor(phase);
        }
        gaitNatural = along;
        gaitRateNow = rate;
        strideFrame = Time.frameCount;
    }

    // Under this share of the legs on their cycles they are setting off (Pace); within SetOffSide of ahead or straight back
    // (degrees) neither foot leads.
    private const float SetOffBelow = .1f, SetOffSide = 20f;

    // The moment of the stride a start takes up (0-1, the left foot down at 0): where the foot on the side the legs go swings
    // past the other, so the first step puts it down that way; ahead or back, the next of the two. From the walk the legs go
    // nearest that way (GaitClips.Gait.RightPass, LeftPass).
    private float SetOffPhase(float legs)
    {
        var set = postureDown >= .5f ? crouchSet : plainSet;
        if (set == null || !Pair(set, legs, out var a, out var b, out float toward)) return phase;
        var gait = (toward > .5f ? b : a).Cycles[0].Gait;
        float side = Mathf.Abs(legs);
        if (side > SetOffSide && side < 180f - SetOffSide) return legs > 0f ? gait.RightPass : gait.LeftPass;
        return Mathf.Repeat(gait.RightPass - phase, 1f) <= Mathf.Repeat(gait.LeftPass - phase, 1f) ? gait.RightPass : gait.LeftPass;
    }

    /// <summary>How the player's own walk or run carries the body over the ground (world, m/s): as far and the way its cycles'
    /// feet go at their pace, the idle's share standing (Pace). The movement goes at it (LocomotionState), so the body goes as
    /// its legs do. False when they didn't walk or run last frame (a clip played out, the air, another state had them).</summary>
    internal bool LegsCarry(out Vector3 velocity)
    {
        velocity = legsVelocity;
        return legsFrame == Time.frameCount - 1;
    }

    /// <summary>This frame's run for the /gait averages: the speed (m/s), steps a minute and how much faster the body goes than
    /// its cycles carry it at their pace (m/s); false when no walk or run cycle played this frame.</summary>
    internal bool TryGaitNow(out float speed, out float steps, out float slide)
    {
        float strideTime = mixWeight > 0f ? mixStride / mixWeight : 0f;
        speed = gaitSpeed;
        steps = strideTime > 0f ? 120f * gaitRateNow / strideTime : 0f;
        slide = Mathf.Max(0f, gaitSpeed - gaitNatural * gaitRateNow);
        return strideFrame == Time.frameCount && mixWeight > 0f;
    }

    /// <summary>The stride as this body runs it now, for the /gait readout: the speed, the speed its cycles carry it at their own
    /// pace, the pace against theirs, steps a minute and how much faster the body goes than they carry it (m/s).</summary>
    internal string GaitReadout()
    {
        TryGaitNow(out _, out float steps, out float slide);
        if (mixWeight <= 0f) slide = 0f;
        var text = new System.Text.StringBuilder();
        text.Append($"속도 {gaitSpeed:0.00} m/s · 클립 제 속도 {gaitNatural:0.00} · 박자 ×{gaitRateNow:0.00}");
        text.Append($" · 분당 {steps:0}걸음 · 미끄러짐 {slide:0.00} m/s · 골반 {yaw:0}° · 다리 {legLength:0.00} m");
        foreach (var channel in channels)
            if (channel.Gait != null && channel.Target > .05f)
                text.Append($"\n  {channel.Playable.GetAnimationClip().OrNull()?.name} {channel.Target:0.00} ({channel.Gait.Speed * GaitScale:0.00} m/s, "
                    + $"분당 {120f / Mathf.Max(.01f, channel.Gait.Stride):0}걸음)");
        return text.ToString();
    }

    // Standing still, the legs keep their place while the view turns, the chest twisting toward it (SpineLook), and once it
    // has gone turnStartAngle away they step round to it as people do (TurnPlanner): the foot on the side of the turn first,
    // carrying most of it, the other foot closing, the hips turning while a foot is in the air and the standing foot
    // pivoting (FootPlanting). At any speed and angle, in any stance and on any body.
    // From 0.9.261 to 0.9.278 the legs stepped round with BIPER's turn-on-the-spot clips instead: whole-body turns made for a
    // body turning from rest to a new heading, 2.27 s, glued on once the view had gone 60° away ("구조와 근본의 문제인 듯").
    // 0.9.279 let the hips turn at their own speed and the feet catch up every 25° of it: four quick steps to a 90° turn
    // ("발을 네 번이나 뚝뚝뚝뚝 하는 건 좀 투머치"); the captured turns take two, and turn with them.
    private void TurnOnTheSpot(bool still, float dt)
    {
        // Another state had the legs meanwhile (a slide, a ladder, a bar): a turn that was under way is over, and the view's
        // turn meanwhile is none of this frame's.
        if (turning == 0 && turnPlanner.Lead != 0) turnPlanner.Reset();
        if (plannedFrame != Time.frameCount - 1) turnPlanner.Forget();
        plannedFrame = Time.frameCount;
        float look = transform.eulerAngles.y;
        legYaw = turnPlanner.Tick(look, legYaw, still, dt, turnStartAngle, turnStepTime, turnStartAngle + 30f);
        turning = turnPlanner.Lead;
        if (!still) return;
        targetYaw = Mathf.DeltaAngle(look, legYaw);
        legsPlanted = true;
    }
}
