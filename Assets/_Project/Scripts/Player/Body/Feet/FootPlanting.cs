using UnityEngine;

/// <summary>
/// Plants the body's feet on the real ground, the way GMod's IK Foot addon does (our port, tuned on our characters).
/// - Each foot looks for the ground under its heel, ankle and toes and stands on the highest, so no part of it sinks
///   into a step. The player's own colliders (its capsule, hitboxes, the held gun) are never ground, as KINEMATION's
///   Character Animation System Foot IK skips its own character's.
/// - Where one stands lower than the capsule's floor (a step down, a slope), the hips drop so it can reach; the other
///   knee bends to meet higher ground. Knees bend about the clip's own knee axis and the leg never turns about its
///   length, so the ankle does not twist (GMod's IK Foot only ever bends thigh and calf on their pitch axis).
/// - Each foot moves by the height of the ground under it, so the clip's own stride and foot lift stay as they are.
/// - Ground and hip heights are kept in world space, so the capsule popping up or down a step never drags a foot
///   through it. Standing, higher ground is taken at once and lower ground settles over the settle time.
/// - Walking or running (Walk, 0.9.390) a foot that comes down (its clip's sole at its floor) keeps the height it came down
///   on until it lifts; a foot that lifts swings from the ground it left to the ground where it will come down (Land: the
///   hips carried on for the time left to its footfall, half a step ahead of them), over what rises between, instead of
///   following the ground under it frame by frame; from half way down its swing it comes down on the ground under it
///   (0.9.392: the guess alone put a running foot's landing a tread or two off on stairs, so it went down through the last of
///   the air 15 to 29 cm in a frame as it touched, the feet record of 2026-11-24 16:31; the user: "계단은 달리면서 이동하면 발이
///   허공에 살짝 뜨는 것 같고"). Along the ground a moving foot is where the clip and the stride have it.
///   The hips stand on what the feet stand on, under the capsule's floor eased (it pops up and down a stair at a time).
///   Until then the ground under a swinging foot was read every frame and taken at once where higher: running up stairs each
///   foot jumped a tread's height in a frame about twenty times a second, and the hips and the eyes rode every pop of the
///   capsule (the experts' review of 2026-11-24; the user: "달리다가 … 발이 텔레포트하거나 디딜 곳을 못 찾거나 … 계단을 뛰어
///   오를 때"). 0.9.390 also kept a moving foot that was down at the place it came down on, and had the hips come down as
///   far as they might for a leg that could not reach it: the run's cycles hold a sole near their floor longer than the
///   body can pass over one place, so at every running step the leg ran out of reach (117% of its length at 6 m/s), the
///   ankle dragged short of its place, the hips dropped 6 cm, and the foot then eased back to the clip's place: a hitch
///   at every step (the feet record of 2026-11-24 16:19; the user: "굉장히 버벅거리고 끊기는 느낌"). 0.9.391 took both out.
/// - No part of the sole comes below the ground: a clip made for other proportions (a deep crouch, toes pointed down on
///   a long foot) has the ankle stand that much higher.
/// - Planted feet tilt to the slope.
/// - Running, the stride can be stretched (Stride): each foot's distance ahead of or behind the hips along the way of
///   travel is scaled, so a cycle made for a slower pace covers the ground the body really covers and the feet stay where
///   they come down instead of skating. The ground is looked for under the stretched foot, and where a leg can't reach
///   that far the hips come down a little (a long stride runs lower), at most Stride.MaxDrop.
/// - In every state, each ankle's turn about the shin and roll to the side stay within the limits of the rest pose
///   (AnkleLimit), so no clip can twist a foot.
/// It measures the legs from the avatar in the rest pose (any humanoid); PlayerBody says each frame whether the feet stand
/// and how far, with its table numbers. The feet after it are read for footstep sounds (PlayerFootsteps).
/// </summary>
internal sealed class FootPlanting
{
    /// <summary>The numbers it plants with (Player.csv, row Body).</summary>
    internal readonly struct Tuning
    {
        public readonly float MaxHipDrop, Reach, SettleTime, MaxTilt, MaxAnkleTurn, MaxAnkleRoll, BlendTime;

        public Tuning(float maxHipDrop, float reach, float settleTime, float maxTilt, float maxAnkleTurn, float maxAnkleRoll, float blendTime)
        {
            MaxHipDrop = maxHipDrop; Reach = reach; SettleTime = settleTime; MaxTilt = maxTilt;
            MaxAnkleTurn = maxAnkleTurn; MaxAnkleRoll = maxAnkleRoll; BlendTime = blendTime;
        }
    }

    /// <summary>
    /// How far this frame's stride is stretched (PlayerBody.Locomotion): along <see cref="Way"/> (world, level, unit) each
    /// foot's distance ahead of or behind the hips is scaled by <see cref="Scale"/>, as much as <see cref="Weight"/>; the
    /// hips come down at most <see cref="MaxDrop"/> (m) for a leg to reach.
    /// </summary>
    internal readonly struct Stride
    {
        public readonly Vector3 Way;
        public readonly float Scale, Weight, MaxDrop;

        public Stride(Vector3 way, float scale, float weight, float maxDrop)
        {
            Way = Vector3.ProjectOnPlane(way, Vector3.up).normalized;
            Scale = scale; Weight = Mathf.Clamp01(weight); MaxDrop = Mathf.Max(0f, maxDrop);
        }

        public static Stride None => default;
        public bool Stretches => Weight > 0f && Mathf.Abs(Scale - 1f) > 1e-3f && Way.sqrMagnitude > .5f;
    }

    /// <summary>
    /// The walk's or run's steps this frame (PlayerBody.Pace): how fast and which way the body goes over the ground (world,
    /// level, m/s), how long a whole stride of both feet takes (s; 0 for none) and where in it the legs are (0 the left foot
    /// coming down, .5 the right; GaitClips' measure, the same on every PC).
    /// </summary>
    internal readonly struct Steps
    {
        public readonly Vector3 Travel;
        public readonly float Cycle, Phase;

        public Steps(Vector3 travel, float cycle, float phase)
        {
            Travel = Vector3.ProjectOnPlane(travel, Vector3.up);
            Cycle = Mathf.Max(0f, cycle);
            Phase = Mathf.Repeat(phase, 1f);
        }

        /// <summary>How long until this foot next comes down (s).</summary>
        public float ToFall(bool left) => Cycle > 0f ? Mathf.Repeat((left ? 0f : .5f) - Phase, 1f) * Cycle : PlainSwing;
    }

    /// <summary>
    /// What keeps a standing foot in its place this frame (PlayerBody.PlantFeet). A foot on the ground belongs to the
    /// ground: it stays where it stands until the body steps or turns. Two things say so here.
    /// <see cref="Carried"/>: how far something that is no step moved the hips sideways this frame (the lean's shift of
    /// the weight, BodyLean.MoveHips); the feet hang from the hips, so that much is taken back out of each foot, always.
    /// <see cref="Stands"/>: the body neither travels nor turns, so each foot that is down keeps the very place it took
    /// when the body came to rest, whatever else moves the hips over it (a gun's lean rolling them, the head followed,
    /// an idle's sway), until the body has turned <see cref="Twist"/> degrees over it or the leg can no longer reach
    /// with the hips down <see cref="MaxDrop"/>; then it steps: it comes up off its place and round to where the stance
    /// has it now over <see cref="StepTime"/> s, <see cref="StepHeight"/> m up at the middle, one foot at a time and the
    /// one on the side the body turns to first (0.9.279). A turn on the spot leads its own steps (<see cref="Lead"/>,
    /// <see cref="LeadShare"/>: TurnPlanner, 0.9.280): that foot steps as far as the turn's step has gone while the other
    /// pivots where it stands. A foot that stops standing (the body sets off) lets go and eases to the clip's place over
    /// <see cref="Release"/> s.
    /// Until 2026-11-18 a foot's height was the ground's and its place was wherever the hips had carried it, so a lean
    /// slid both feet sideways with the pelvis (the user: "제자리에서 상체만 기울어야 하나 ... 발(하체)까지 옆으로 이동").
    /// </summary>
    internal readonly struct Hold
    {
        public readonly bool Stands;
        public readonly Vector3 Carried;
        public readonly float Yaw, Twist, MaxDrop, Release, StepTime, StepHeight, LeadShare;
        // The foot a turn on the spot steps: 0 none, 1 the left, 2 the right.
        public readonly int Lead;

        public Hold(bool stands, Vector3 carried, float yaw, float twist, float maxDrop, float release, float stepTime, float stepHeight,
            int lead = 0, float leadShare = 0f)
        {
            Lead = lead;
            LeadShare = Mathf.Clamp01(leadShare);
            Stands = stands;
            Carried = Vector3.ProjectOnPlane(carried, Vector3.up);
            Yaw = yaw; Twist = Mathf.Max(1f, twist); MaxDrop = Mathf.Max(0f, maxDrop); Release = Mathf.Max(.01f, release);
            StepTime = Mathf.Max(.05f, stepTime); StepHeight = Mathf.Max(0f, stepHeight);
        }
    }

    internal sealed class Leg
    {
        public Transform Upper, Lower, Foot;
        /// <summary>Where the stride's stretch moves this foot this frame (world, level; zero without a stretch).</summary>
        public Vector3 Shift;
        /// <summary>The foot keeps its place (Hold): where it stands (world, level) and the body's yaw when it took it.</summary>
        public bool Kept;
        public Vector3 Place;
        public float KeptYaw;
        /// <summary>How far the foot stands from where the clip has it (world, level): the kept place's, or what is left of it easing away.</summary>
        public Vector3 Off;
        /// <summary>A step under way (Hold): how far through it (0-1), where it left from (world, level), and how high the
        /// foot is off the ground this frame (m).</summary>
        public bool Stepping, Led;
        public float StepClock, StepLift;
        public Vector3 StepFrom;
        public AnkleLimit Ankle;
        /// <summary>The ankle's height above the sole, measured in the rest pose (m).</summary>
        public float Sole;
        /// <summary>Heel and toe tip in the foot bone's space; only their place over the ground is used.</summary>
        public Vector3 Heel, Tip;
        /// <summary>The sole under the heel and under the toe tip, in the foot bone's space (on the floor in the rest pose).</summary>
        public Vector3 HeelSole, TipSole;
        /// <summary>Smoothed world height of the ground under the foot (m).</summary>
        public float Ground;
        /// <summary>Walking or running (Walk): the foot is down and keeps the ground it came down on; else it swings from
        /// LiftGround to LandGround (easing to LandTarget, refreshed while it swings) over SwingTime, Clearance higher at the
        /// middle. Supported: there is ground where it stands or will come down.</summary>
        public bool Down, Supported;
        public float LiftGround, LandGround, LandTarget, Clearance, SwingTime, SwingClock;
        public int LandRefresh;
        /// <summary>The collider under the foot, or null when it has no support.</summary>
        public Collider Surface;
        public float SupportWeight;
        public Vector3 Normal = Vector3.up, Target;
    }

    public Leg Left { get; }
    public Leg Right { get; }
    /// <summary>How far the feet stand on the ground this frame (eased in and out).</summary>
    public float Weight { get; private set; }
    /// <summary>The feet stand on the ground this frame.</summary>
    public bool OnGround { get; private set; }

    // Hits of one ground probe; shared, since the feet are planted one body at a time on the main thread.
    private static readonly RaycastHit[] Hits = new RaycastHit[8];

    private readonly Transform body, owner;
    // How far over the body's root its drawn soles are (m): where the floor of the clips and of the ankles' heights is.
    private readonly float sole;
    private float hipFloor, lastFloor, strideDrop, smoothFloor;
    private bool settled;

    /// <summary>For the feet record (PlayerBody.RecordFeet): where the hips' floor stands (world height), the capsule's floor
    /// eased, and how far the stride's reach has the hips down (m).</summary>
    public float HipFloor => hipFloor;
    public float SmoothFloor => smoothFloor;
    public float StrideDrop => strideDrop;

    // The capsule's floor is followed this quickly for the hips (s): it pops up and down a stair at a time.
    private const float FloorFollow = .1f;
    // A foot that is down lifts once its clip's sole is this much higher than it came down at (m), so a sole that hovers at the
    // line doesn't come down and up frame by frame.
    private const float LiftsFrom = .015f;
    // A swinging foot's landing is looked for again every this many frames; it takes the new ground this quickly (s); a swing
    // lasts at least this long (s), and one the stride can't time this long (s); it rises at most this far over what lies
    // between (m).
    private const int LandEvery = 3;
    private const float LandEase = .05f, LeastSwing = .06f, PlainSwing = .3f, MostClearance = .3f;
    // From this share of its swing a foot comes down on the ground under it rather than on the guess (Walk); the guess puts the
    // foot ahead of the hips by a quarter of the stride but never further than a foot lands ahead of them (m): a sprint's
    // quarter stride is 0.9 m, its foot lands about 0.35 m ahead.
    private const float LateSwing = .5f, LandLeadMost = .4f;

    // The stride's hips follow a reach this quickly (s): fast enough for a step, slow enough that one frame's reach never
    // pops the hips (and the camera on the eyes).
    private const float StrideDropTime = .05f;
    // A leg reaches at most this share of its length, so the knee never locks straight.
    private const float StrideReach = .985f;

    private FootPlanting(Transform body, Transform owner, float sole, Leg left, Leg right)
    {
        this.body = body;
        this.owner = owner;
        this.sole = sole;
        Left = left;
        Right = right;
        Vector3 hips = right.Upper.position - left.Upper.position;
        left.Ankle = AnkleLimit.Create(left.Foot, hips);
        right.Ankle = AnkleLimit.Create(right.Foot, hips);
    }

    /// <summary>
    /// Measures both legs of <paramref name="animator"/>'s humanoid in its rest pose, or returns null for a body without
    /// legs. <paramref name="forward"/> is the way the body faces at rest; <paramref name="owner"/> is the player, whose
    /// own colliders are never ground. <paramref name="sole"/> is how far over the body's root the soles it draws are (m):
    /// a body stood on its shoes' soles rather than its model's floor (PlayerBody.StandOnSoles) has its root that much
    /// under the ground, and the ankles stand their height over those soles.
    /// </summary>
    public static FootPlanting Measure(Animator animator, Transform body, Transform owner, Vector3 forward, float sole = 0f)
    {
        if (animator == null || !animator.isHuman) return null;
        var left = MakeLeg(animator, body, forward, true, sole);
        var right = MakeLeg(animator, body, forward, false, sole);
        return left != null && right != null ? new FootPlanting(body, owner, sole, left, right) : null;
    }

    private static Leg MakeLeg(Animator animator, Transform body, Vector3 forward, bool left, float sole)
    {
        float floorY = body.position.y + sole;
        Transform Bone(HumanBodyBones l, HumanBodyBones r) => animator.GetBoneTransform(left ? l : r);
        var upper = Bone(HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg);
        var lower = Bone(HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg);
        var foot = Bone(HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot);
        if (upper == null || lower == null || foot == null) return null;
        // Measured in the rest pose: the ball of the foot is the toe joint, or about twice the ankle's height ahead.
        var toes = Bone(HumanBodyBones.LeftToes, HumanBodyBones.RightToes);
        Vector3 ankle = foot.position;
        if (forward.sqrMagnitude < .5f) forward = body.forward;
        Vector3 ball = toes != null ? toes.position : ankle + forward * 2f * Mathf.Max(.04f, ankle.y - floorY);
        Vector3 along = Vector3.ProjectOnPlane(ball - ankle, Vector3.up);
        if (along.sqrMagnitude < 1e-4f) along = Vector3.ProjectOnPlane(forward, Vector3.up).normalized * .12f;
        // The toe tip reaches as far as the toe bones do (some rigs end in a toe-end bone), past the ball either way.
        float ballDistance = along.magnitude, tipDistance = ballDistance * 1.4f;
        if (toes != null)
            foreach (var bone in toes.GetComponentsInChildren<Transform>())
                tipDistance = Mathf.Max(tipDistance, Vector3.Dot(bone.position - ankle, along / ballDistance));
        tipDistance = Mathf.Min(tipDistance, ballDistance * 2.5f);
        Vector3 floor = Vector3.down * (ankle.y - floorY);
        return new Leg
        {
            Upper = upper, Lower = lower, Foot = foot, Sole = ankle.y - floorY,
            Heel = foot.InverseTransformPoint(ankle - along * .35f), Tip = foot.InverseTransformPoint(ankle + along * 1.4f),
            HeelSole = foot.InverseTransformPoint(ankle - along * .35f + floor),
            TipSole = foot.InverseTransformPoint(ankle + along / ballDistance * tipDistance + floor)
        };
    }

    /// <summary>The body lies limp or starts over: the feet let go of the ground at once.</summary>
    public void LetGo()
    {
        Weight = 0f;
        settled = OnGround = false;
    }

    /// <summary>
    /// The feet on the ground this frame: each at the ground's height under it, the stride stretched by
    /// <paramref name="stride"/> while the feet stand, and each standing foot kept where it stands as
    /// <paramref name="hold"/> says.
    /// </summary>
    public void Plant(bool onFeet, float floor, LayerMask ground, in Tuning tuning, float dt, in Stride stride, in Hold hold, in Steps steps)
    {
        // In every state, whatever the clip or the IK does, an ankle stays within what a real one can do: held once so a
        // planted foot is levelled from a sane foot, and again after the legs have bent.
        HoldAnkles(tuning);
        StandOnGround(onFeet, floor, ground, tuning, dt, stride, hold, steps);
        HoldAnkles(tuning);
    }

    private void HoldAnkles(in Tuning tuning)
    {
        Left.Ankle?.Apply(tuning.MaxAnkleTurn, tuning.MaxAnkleRoll);
        Right.Ankle?.Apply(tuning.MaxAnkleTurn, tuning.MaxAnkleRoll);
    }

    private void StandOnGround(bool onFeet, float floor, LayerMask ground, in Tuning tuning, float dt, in Stride stride, in Hold hold, in Steps steps)
    {
        OnGround = onFeet;
        Weight = Mathf.MoveTowards(Weight, onFeet ? 1f : 0f, dt / tuning.BlendTime);
        // The floor the clip stands the soles on: the root, and the drawn soles over it (Measure).
        float clipFloor = body.position.y + sole;
        StretchStride(stride);
        if (Weight <= 0f)
        {
            settled = false;
            strideDrop = 0f;
            Left.Kept = Right.Kept = Left.Stepping = Right.Stepping = Left.Led = Right.Led = false;
            Left.Down = Right.Down = false;
            Left.SwingTime = Right.SwingTime = 0f;
            Left.StepLift = Right.StepLift = 0f;
            Left.Off = Right.Off = Vector3.zero;
            return;
        }

        // A clip made for other proportions can put a foot through the floor: a deep crouch holds the ankle lower than the
        // sole lets it stand, and pointing the toes down drives a longer foot's toe tip in. No part of the sole comes
        // below the ground; the ankle stands that much higher, and the knee bends for it.
        float leftDepth = SoleDepth(Left), rightDepth = SoleDepth(Right);
        float leftLift = Mathf.Max(Left.Foot.position.y - clipFloor, leftDepth);
        float rightLift = Mathf.Max(Right.Foot.position.y - clipFloor, rightDepth);
        float lowerLift = Mathf.Min(leftLift, rightLift);
        // How far the clip holds each sole over its floor. The sole, not the ankle: a foot on its toes with the heel up
        // (a crouch) is as much on the ground as a flat one, and its ankle stands centimetres higher.
        float leftOver = leftLift - leftDepth, rightOver = rightLift - rightDepth, lowerOver = Mathf.Min(leftOver, rightOver);

        if (onFeet)
        {
            float follow = settled ? 1f - Mathf.Exp(-dt / tuning.SettleTime) : 1f;
            // The capsule's floor eased: it pops up and down a stair at a time, and the hips, and the eyes on them, follow the
            // feet's ground under it rather than its pops.
            smoothFloor = settled
                ? Mathf.Clamp(Mathf.Lerp(smoothFloor, floor, 1f - Mathf.Exp(-dt / FloorFollow)), floor - tuning.MaxHipDrop, floor + tuning.MaxHipDrop)
                : floor;
            if (!hold.Stands)
            {
                Walk(Left, true, leftOver, floor, ground, tuning, steps, dt);
                Walk(Right, false, rightOver, floor, ground, tuning, steps, dt);
            }
            else
            {
                Left.Down = Right.Down = false;
                Left.SwingTime = Right.SwingTime = 0f;
                Probe(Left, floor, ground, follow, tuning, dt);
                Probe(Right, floor, ground, follow, tuning, dt);
            }
            // A missing support fades toward the clip instead of snapping a leg or the hips.
            float lowest = Mathf.Min(Mathf.Lerp(smoothFloor, Left.Ground, Left.SupportWeight),
                Mathf.Lerp(smoothFloor, Right.Ground, Right.SupportWeight));
            lowest = Mathf.Clamp(lowest, smoothFloor - tuning.MaxHipDrop, smoothFloor);
            hipFloor = Mathf.Clamp(Mathf.Lerp(hipFloor, lowest, follow), smoothFloor - tuning.MaxHipDrop, smoothFloor);
            settled = true;
        }
        else
        {
            // Leaving the ground: hold the last heights relative to the capsule while the weight fades.
            float moved = floor - lastFloor;
            hipFloor += moved;
            smoothFloor = floor;
            Left.Ground += moved;
            Right.Ground += moved;
        }
        lastFloor = floor;
        body.position += Vector3.up * ((hipFloor - clipFloor) * Weight);
        // Where each foot stands, level: the clip's place with the stride's stretch, the place it keeps, or where a step has
        // it (Hold). Turning to the right the right foot leads, to the left the left one.
        Vector3 leftAt, rightAt;
        if (hold.Lead == 2 || hold.Lead == 0 && Mathf.DeltaAngle(Left.Kept ? Left.KeptYaw : hold.Yaw, hold.Yaw) > 0f)
        {
            rightAt = Stand(Right, Left, 2, rightLift, rightOver - lowerOver, onFeet, hold, dt);
            leftAt = Stand(Left, Right, 1, leftLift, leftOver - lowerOver, onFeet, hold, dt);
        }
        else
        {
            leftAt = Stand(Left, Right, 1, leftLift, leftOver - lowerOver, onFeet, hold, dt);
            rightAt = Stand(Right, Left, 2, rightLift, rightOver - lowerOver, onFeet, hold, dt);
        }
        // A stretched stride, or hips that went over away from where a foot stands (a kept place, the lean's shift), can
        // put a foot further from its hip than the leg reaches: the hips come down that much, as far as each allows.
        float reach = Mathf.Max(ReachDrop(Left, leftLift, leftAt), ReachDrop(Right, rightLift, rightAt));
        bool held = hold.Carried.sqrMagnitude >= 1e-8f || Left.Off.sqrMagnitude >= 1e-8f || Right.Off.sqrMagnitude >= 1e-8f;
        reach = Mathf.Min(reach, Mathf.Max(stride.Stretches ? stride.MaxDrop : 0f, held ? hold.MaxDrop : 0f));
        strideDrop = Mathf.Lerp(strideDrop, reach, 1f - Mathf.Exp(-dt / StrideDropTime));
        body.position += Vector3.down * (strideDrop * Weight);
        Vector3 hips = (Right.Upper.position - Left.Upper.position).normalized;
        // A stepping foot is that much off the ground, and swings at the clip's angle rather than lying on the slope.
        Place(Left, leftLift + Left.StepLift, leftLift + Left.StepLift - lowerLift, hips, tuning, leftAt);
        Place(Right, rightLift + Right.StepLift, rightLift + Right.StepLift - lowerLift, hips, tuning, rightAt);
    }

    // The clip has a foot down when its sole is within this of its lower foot's (m); a foot whose place has eased to
    // within this of the clip's has arrived and may take a place again (m).
    private const float DownHeight = .03f, Arrived = .005f;

    // Where this foot stands this frame (world; only its level place is used). The clip's place is the foot's with what
    // carried the hips sideways taken back out and the stride's stretch added. While the body stands, a foot that is
    // down keeps the place it took as the body came to rest; turned too far over, or out of the leg's reach, it steps:
    // up off its place, round to where the clip has it now and down, then keeps that place. One foot steps at a time
    // (<paramref name="other"/>), unless this one can no longer reach where it stands. Until 0.9.279 a foot let go and
    // slid to its new place on the ground, which is why the turns on the spot were clips (0.9.261-0.9.278).
    // Setting off, a foot that stood keeps its place until its clip lifts it, or its leg can no longer reach it: the walk's
    // first step leaves from where the foot stood, the other keeps its place until its own lift, and each lets go in the air.
    // Until 0.9.396 both let go the moment the body moved (Body.footHoldSpeed) and slid to the clip's places on the ground
    // over Body.footHoldRelease, and the legs flicked at every start (the user, 2026-11-24: "걷기 시작할 때 다리가 튕겨나").
    private Vector3 Stand(Leg leg, Leg other, int side, float lift, float over, bool onFeet, in Hold hold, float dt)
    {
        Vector3 clip = leg.Foot.position - hold.Carried + leg.Shift;
        bool settingOff = !hold.Stands && leg.Kept;
        bool keep = onFeet && (hold.Stands || settingOff) && over < (settingOff ? DownHeight + LiftsFrom : DownHeight)
            && leg.SupportWeight > 0f && leg.Shift.sqrMagnitude < 1e-8f;
        // A step's lift eases off as its place does when the step is cut short (the body set off or left the ground): until
        // 0.9.400 it went to nothing in a frame and the stepping foot dropped up to 8 cm (the audit of 2026-11-24). A step
        // under way sets it again below.
        leg.StepLift *= hold.Release > 0f ? Mathf.Exp(-dt / hold.Release) : 0f;
        // A step the turn on the spot leads (TurnPlanner): up off its place and round to where the clip has it under the
        // hips' new heading, as far as the turn's step has gone; down there once the step is over.
        if (keep && hold.Lead == side && hold.LeadShare < 1f)
        {
            if (!leg.Stepping || !leg.Led)
            {
                leg.StepFrom = leg.Kept ? leg.Place : clip + leg.Off;
                leg.Kept = false;
                leg.Stepping = leg.Led = true;
            }
            float share = hold.LeadShare, swing = share * share * (3f - 2f * share);
            leg.StepClock = share;
            leg.StepLift = hold.StepHeight * Mathf.Sin(Mathf.PI * share);
            leg.Off = Vector3.ProjectOnPlane(Vector3.Lerp(leg.StepFrom, clip, swing) - clip, Vector3.up);
            return clip + leg.Off;
        }
        if (leg.Stepping && leg.Led)
        {
            leg.Stepping = leg.Led = false;
            if (keep)
            {
                leg.Kept = true;
                leg.Place = clip;
                leg.KeptYaw = hold.Yaw;
                leg.Off = Vector3.zero;
                return clip;
            }
        }
        if (leg.Stepping)
        {
            // The body set off or left the ground mid-step: the foot eases on to the clip from where the step had it.
            if (!keep) leg.Stepping = false;
            else
            {
                leg.StepClock = Mathf.Min(1f, leg.StepClock + dt / hold.StepTime);
                float s = leg.StepClock, along = s * s * (3f - 2f * s);
                leg.StepLift = hold.StepHeight * Mathf.Sin(Mathf.PI * s);
                leg.Off = Vector3.ProjectOnPlane(Vector3.Lerp(leg.StepFrom, clip, along) - clip, Vector3.up);
                if (s >= 1f)
                {
                    leg.Stepping = false;
                    leg.Kept = true;
                    leg.Place = clip;
                    leg.KeptYaw = hold.Yaw;
                    leg.Off = Vector3.zero;
                }
                return clip + leg.Off;
            }
        }
        if (!keep) leg.Kept = false;
        // Setting off it lets go once its leg can't reach it; it never steps (a step is the standing body's).
        else if (settingOff)
        {
            if (OutOfReach(leg, lift, leg.Place, hold.MaxDrop)) leg.Kept = false;
        }
        else if (leg.Kept)
        {
            // While a turn leads the steps the standing foot pivots where it stands, however far the hips turn over it, and
            // stands at that turn after: the captured turns leave it so (a third step to undo the pivot made 90° three steps).
            if (hold.Lead != 0) leg.KeptYaw = hold.Yaw;
            bool far = OutOfReach(leg, lift, leg.Place, hold.MaxDrop);
            bool twisted = hold.Lead == 0 && Mathf.Abs(Mathf.DeltaAngle(leg.KeptYaw, hold.Yaw)) > hold.Twist;
            if ((far || twisted) && (!other.Stepping || far))
            {
                leg.Kept = false;
                leg.Stepping = true;
                leg.StepClock = 0f;
                leg.StepFrom = leg.Place;
                leg.Off = Vector3.ProjectOnPlane(leg.Place - clip, Vector3.up);
                return clip + leg.Off;
            }
        }
        else if (leg.Off.sqrMagnitude < Arrived * Arrived)
        {
            leg.Kept = true;
            leg.Place = clip + leg.Off;
            leg.KeptYaw = hold.Yaw;
        }
        if (leg.Kept) leg.Off = Vector3.ProjectOnPlane(leg.Place - clip, Vector3.up);
        else leg.Off *= hold.Release > 0f ? Mathf.Exp(-dt / hold.Release) : 0f;
        return clip + leg.Off;
    }

    // Whether the leg cannot reach a place on the ground even with the hips as far down as they may go for it.
    private static bool OutOfReach(Leg leg, float lift, Vector3 place, float maxDrop) => Added(leg, lift, place) > maxDrop;

    // How far the hips must come down from where they are for this leg to reach a place on the ground (m): negative when
    // the leg has that much to spare, infinite when the place is further off, level, than the leg is long.
    private static float Need(Leg leg, float lift, Vector3 place)
    {
        float most = ((leg.Lower.position - leg.Upper.position).magnitude + (leg.Foot.position - leg.Lower.position).magnitude) * StrideReach;
        Vector3 from = leg.Upper.position;
        float level = new Vector2(place.x - from.x, place.z - from.z).sqrMagnitude;
        return level >= most * most ? float.PositiveInfinity : from.y - (leg.Ground + lift) - Mathf.Sqrt(most * most - level);
    }

    // What a place off the clip's asks of the hips beyond what the clip's own leg already asks (m). A clip that stands a
    // leg straighter than the reach allows (most standing idles) is not made to bend it, so this grows from nothing as
    // the place leaves the clip's, and the hips (and the eyes on them) do not dip the moment a lean begins.
    private static float Added(Leg leg, float lift, Vector3 place)
    {
        float need = Need(leg, lift, place);
        if (float.IsPositiveInfinity(need)) return need;
        float own = Need(leg, lift, leg.Foot.position);
        return need - (float.IsPositiveInfinity(own) ? 0f : Mathf.Max(0f, own));
    }

    // Where the stretched stride moves each foot: its distance ahead of or behind the hips along the way of travel, scaled.
    // The swinging foot is moved the same way, so it reaches as far forward as the planted one goes back.
    private void StretchStride(in Stride stride)
    {
        Left.Shift = Right.Shift = Vector3.zero;
        if (!stride.Stretches) return;
        Vector3 hips = (Left.Upper.position + Right.Upper.position) * .5f;
        Left.Shift = Shift(Left, stride, hips);
        Right.Shift = Shift(Right, stride, hips);

        static Vector3 Shift(Leg leg, in Stride stride, Vector3 hips)
            => stride.Way * (Vector3.Dot(leg.Foot.position - hips, stride.Way) * (stride.Scale - 1f) * stride.Weight);
    }

    // How far the hips must come down for this leg to reach where its foot stands (m). A foot standing where the hips
    // have it needs none. A stretched stride asks for all the leg lacks, as it has since 0.8.260 (its look was tuned so);
    // a place the hips went over away from (a kept place, the lean's shift) only for what it adds to the clip's own.
    private float ReachDrop(Leg leg, float lift, Vector3 at)
    {
        if (leg.SupportWeight <= 0f) return 0f;
        float need;
        if (leg.Shift.sqrMagnitude >= 1e-8f) need = Need(leg, lift, at);
        else
        {
            Vector3 off = at - leg.Foot.position;
            if (off.x * off.x + off.z * off.z < 1e-8f) return 0f;
            need = Added(leg, lift, at);
        }
        return float.IsPositiveInfinity(need) ? 0f : Mathf.Max(0f, need) * leg.SupportWeight;
    }

    // How high the ankle has to stand for the heel and the toe tip to stay on or above the ground, the foot turned as the
    // clip has it. Flat as in the rest pose, it is the ankle's own height over the sole.
    private static float SoleDepth(Leg leg)
    {
        float ankle = leg.Foot.position.y;
        return Mathf.Max(leg.Sole, ankle - leg.Foot.TransformPoint(leg.HeelSole).y, ankle - leg.Foot.TransformPoint(leg.TipSole).y);
    }

    private void Probe(Leg leg, float floor, LayerMask ground, float follow, in Tuning tuning, float dt)
    {
        float height = floor;
        Vector3 normal = Vector3.up;
        Collider surface = null;
        bool found = false;
        float reach = tuning.Reach;
        // Under the foot where the stretched stride puts it.
        found = Underfoot(leg, floor, ground, reach, out height, out normal, out surface);
        float previousWeight = leg.SupportWeight;
        leg.SupportWeight = Mathf.MoveTowards(previousWeight, found ? 1f : 0f, dt / Mathf.Max(.01f, tuning.SettleTime));
        if (!found)
        {
            leg.Surface = null;
            // Keep the last target relative to the capsule while its influence fades.
            leg.Ground += floor - lastFloor;
            return;
        }
        leg.Ground = previousWeight <= 0f || height > leg.Ground ? height : Mathf.Lerp(leg.Ground, height, follow);
        leg.Normal = previousWeight <= 0f ? normal : Vector3.Slerp(leg.Normal, normal, follow);
        leg.Surface = surface;
    }

    // The highest ground under the foot's heel, ankle and toe tip where the stretched stride puts it.
    private bool Underfoot(Leg leg, float floor, LayerMask ground, float reach, out float height, out Vector3 normal, out Collider surface)
    {
        height = floor;
        normal = Vector3.up;
        surface = null;
        bool found = false;
        Take(leg.Foot.TransformPoint(leg.Heel) + leg.Shift, ref found, ref height, ref normal, ref surface);
        Take(leg.Foot.position + leg.Shift, ref found, ref height, ref normal, ref surface);
        Take(leg.Foot.TransformPoint(leg.Tip) + leg.Shift, ref found, ref height, ref normal, ref surface);
        return found;

        void Take(Vector3 point, ref bool found, ref float height, ref Vector3 normal, ref Collider surface)
        {
            if (!GroundAt(point, floor, ground, reach, out float at, out Vector3 facing, out Collider what) || (found && at <= height)) return;
            height = at;
            normal = facing;
            surface = what;
            found = true;
        }
    }

    // The first thing below the point, within the reach of the capsule's floor, that isn't the player itself, if it is something
    // to stand on (not a wall's side).
    private bool GroundAt(Vector3 point, float floor, LayerMask ground, float reach, out float height, out Vector3 normal, out Collider surface)
    {
        height = floor;
        normal = Vector3.up;
        surface = null;
        int count = Physics.RaycastNonAlloc(new Vector3(point.x, floor + reach, point.z), Vector3.down, Hits, reach * 2f,
            ground, QueryTriggerInteraction.Ignore);
        int nearest = -1;
        for (int i = 0; i < count; i++)
            if ((nearest < 0 || Hits[i].distance < Hits[nearest].distance) && !Hits[i].collider.transform.IsChildOf(owner))
                nearest = i;
        if (nearest < 0 || Hits[nearest].normal.y <= .5f) return false;
        height = Hits[nearest].point.y;
        normal = Hits[nearest].normal;
        surface = Hits[nearest].collider;
        return true;
    }

    // Walking or running, the ground a foot stands on: a foot coming down (its clip's sole at its floor) takes the ground under
    // it and keeps it until it lifts; a foot that lifts swings from the ground it left to the ground where it will come down
    // (Land), smoothly, over what rises between, rather than the ground under it frame by frame.
    private void Walk(Leg leg, bool left, float over, float floor, LayerMask ground, in Tuning tuning, in Steps steps, float dt)
    {
        float settle = dt / Mathf.Max(.01f, tuning.SettleTime);
        if (over < (leg.Down ? DownHeight + LiftsFrom : DownHeight))
        {
            if (!leg.Down)
            {
                leg.Down = true;
                leg.Supported = Underfoot(leg, floor, ground, tuning.Reach, out float height, out Vector3 normal, out Collider surface);
                leg.Surface = surface;
                if (leg.Supported)
                {
                    leg.Ground = height;
                    leg.Normal = normal;
                }
            }
            leg.SupportWeight = Mathf.MoveTowards(leg.SupportWeight, leg.Supported ? 1f : 0f, settle);
            return;
        }
        if (leg.Down || leg.SwingTime <= 0f)
        {
            leg.Down = false;
            leg.LiftGround = leg.SupportWeight > 0f ? leg.Ground : floor;
            leg.SwingTime = Mathf.Max(LeastSwing, steps.ToFall(left));
            leg.SwingClock = 0f;
            Land(leg, left, floor, ground, tuning.Reach, steps, true);
        }
        leg.SwingClock += dt;
        float s = Mathf.Clamp01(leg.SwingClock / leg.SwingTime), eased = s * s * (3f - 2f * s);
        if (s >= LateSwing)
        {
            // Coming down: the ground under the foot, eased in, so it touches what is there.
            if (Underfoot(leg, floor, ground, tuning.Reach, out float under, out Vector3 normal, out Collider surface))
            {
                leg.Supported = true;
                leg.LandTarget = under;
                leg.Normal = normal;
                leg.Surface = surface;
            }
        }
        else if (--leg.LandRefresh <= 0) Land(leg, left, floor, ground, tuning.Reach, steps, false);
        leg.LandGround = Mathf.Lerp(leg.LandGround, leg.LandTarget, 1f - Mathf.Exp(-dt / LandEase));
        leg.Ground = Mathf.Lerp(leg.LiftGround, leg.LandGround, eased) + leg.Clearance * Mathf.Sin(Mathf.PI * s);
        leg.SupportWeight = Mathf.MoveTowards(leg.SupportWeight, leg.Supported ? 1f : 0f, settle);
    }

    // Where a swinging foot will come down: the hips carried on at the travel's speed for the time left to its footfall, half a
    // step ahead of them (at most LandLeadMost), the foot's own way aside of them; and the ground there. As the swing begins also how far the ground
    // between rises over the straight way from where it left (Clearance), from three rays along it.
    private void Land(Leg leg, bool left, float floor, LayerMask ground, float reach, in Steps steps, bool start)
    {
        leg.LandRefresh = LandEvery;
        Vector3 hips = (Left.Upper.position + Right.Upper.position) * .5f;
        Vector3 travel = steps.Travel;
        float speed = travel.magnitude;
        Vector3 way = speed > .1f ? travel / speed : Vector3.ProjectOnPlane(body.forward, Vector3.up).normalized;
        Vector3 foot = leg.Foot.position + leg.Shift;
        Vector3 aside = Vector3.ProjectOnPlane(foot - hips, Vector3.up);
        aside -= way * Vector3.Dot(aside, way);
        float remaining = Mathf.Max(0f, leg.SwingTime - leg.SwingClock);
        Vector3 at = new Vector3(hips.x, foot.y, hips.z) + travel * remaining + way * Mathf.Min(speed * steps.Cycle * .25f, LandLeadMost) + aside;
        leg.Supported = GroundAt(at, floor, ground, reach, out float height, out Vector3 normal, out Collider surface);
        leg.LandTarget = leg.Supported ? height : leg.LiftGround;
        if (leg.Supported)
        {
            leg.Surface = surface;
            leg.Normal = normal;
        }
        if (!start) return;
        leg.LandGround = leg.LandTarget;
        leg.Clearance = 0f;
        for (int i = 1; i <= 3; i++)
        {
            float share = i * .25f;
            if (GroundAt(Vector3.Lerp(foot, at, share), floor, ground, reach, out float between, out _, out _))
                leg.Clearance = Mathf.Max(leg.Clearance, between - Mathf.Lerp(leg.LiftGround, leg.LandGround, share * share * (3f - 2f * share)));
        }
        leg.Clearance = Mathf.Min(leg.Clearance, MostClearance);
    }

    // lift: how high the clip holds this ankle above its own floor. above: how far over the clip's lower foot.
    // hips: from the left hip joint to the right one. at: where the foot stands, level (Stand).
    private void Place(Leg leg, float lift, float above, Vector3 hips, in Tuning tuning, Vector3 at)
    {
        Vector3 foot = leg.Foot.position;
        float weight = Weight * leg.SupportWeight;
        if (weight <= 0f)
        {
            leg.Target = foot;
            return;
        }
        Quaternion rotation = leg.Foot.rotation;
        // Its place and the ground's height as far as the feet stand (both were worked out at full weight).
        leg.Target = new Vector3(Mathf.Lerp(foot.x, at.x, weight), Mathf.Lerp(foot.y, leg.Ground + lift, weight), Mathf.Lerp(foot.z, at.z, weight));
        // The knee bends about the clip's own knee axis. A nearly straight leg has no clear axis, so it bends
        // forward about the hips' sideways line instead (a clip knee bent slightly backward counts as straight).
        Vector3 thigh = leg.Lower.position - leg.Upper.position, shin = foot - leg.Lower.position;
        Vector3 own = Vector3.Cross(thigh, shin), straight = Vector3.ProjectOnPlane(hips, thigh).normalized;
        float bent = Mathf.Clamp01(own.magnitude / Mathf.Max(1e-6f, thigh.magnitude * shin.magnitude) / .26f);
        if (Vector3.Dot(own, straight) < 0f && bent < 1f) bent = 0f;
        TwoBoneIK.Reach(leg.Upper, leg.Lower, leg.Foot, leg.Target, Vector3.Lerp(straight, own.normalized, bent));
        // A foot on the ground lies along the slope; a swinging one keeps the clip's angle.
        float planted = (1f - Mathf.Clamp01(above / .1f)) * weight;
        Quaternion tilt = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, leg.Normal), tuning.MaxTilt);
        leg.Foot.rotation = Quaternion.Slerp(rotation, tilt * rotation, planted);
    }

    /// <summary>Draws the ground each foot found (green, red without support) and where its ankle goes (yellow).</summary>
    public void DrawGizmos()
    {
        if (Weight <= 0f) return;
        Draw(Left);
        Draw(Right);

        static void Draw(Leg leg)
        {
            if (leg == null || leg.Foot == null) return;
            Vector3 heel = leg.Foot.TransformPoint(leg.Heel), tip = leg.Foot.TransformPoint(leg.Tip);
            Gizmos.color = leg.Surface != null ? Color.green : Color.red;
            Gizmos.DrawLine(new Vector3(heel.x, leg.Ground, heel.z), new Vector3(tip.x, leg.Ground, tip.z));
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(leg.Target, .03f);
        }
    }
}
