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
///   through it. Higher ground is taken at once; lower ground and the hips settle over the settle time.
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
    /// What keeps a standing foot in its place this frame (PlayerBody.PlantFeet). A foot on the ground belongs to the
    /// ground: it stays where it stands until the body steps or turns. Two things say so here.
    /// <see cref="Carried"/>: how far something that is no step moved the hips sideways this frame (the lean's shift of
    /// the weight, BodyLean.MoveHips); the feet hang from the hips, so that much is taken back out of each foot, always.
    /// <see cref="Stands"/>: the body neither travels nor turns, so each foot that is down keeps the very place it took
    /// when the body came to rest, whatever else moves the hips over it (a gun's lean rolling them, the head followed,
    /// an idle's sway), until the body has turned <see cref="Twist"/> degrees over it or the leg can no longer reach
    /// with the hips down <see cref="MaxDrop"/>; then it steps: it comes up off its place and round to where the stance
    /// has it now over <see cref="StepTime"/> s, <see cref="StepHeight"/> m up at the middle, one foot at a time and the
    /// one on the side the body turns to first (0.9.279). A foot that stops standing (the body sets off) lets go and eases
    /// to the clip's place over <see cref="Release"/> s.
    /// Until 2026-11-18 a foot's height was the ground's and its place was wherever the hips had carried it, so a lean
    /// slid both feet sideways with the pelvis (the user: "제자리에서 상체만 기울어야 하나 ... 발(하체)까지 옆으로 이동").
    /// </summary>
    internal readonly struct Hold
    {
        public readonly bool Stands;
        public readonly Vector3 Carried;
        public readonly float Yaw, Twist, MaxDrop, Release, StepTime, StepHeight;

        public Hold(bool stands, Vector3 carried, float yaw, float twist, float maxDrop, float release, float stepTime, float stepHeight)
        {
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
        public bool Stepping;
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
    private float hipFloor, lastFloor, strideDrop;
    private bool settled;

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
    public void Plant(bool onFeet, float floor, LayerMask ground, in Tuning tuning, float dt, in Stride stride, in Hold hold)
    {
        // In every state, whatever the clip or the IK does, an ankle stays within what a real one can do: held once so a
        // planted foot is levelled from a sane foot, and again after the legs have bent.
        HoldAnkles(tuning);
        StandOnGround(onFeet, floor, ground, tuning, dt, stride, hold);
        HoldAnkles(tuning);
    }

    private void HoldAnkles(in Tuning tuning)
    {
        Left.Ankle?.Apply(tuning.MaxAnkleTurn, tuning.MaxAnkleRoll);
        Right.Ankle?.Apply(tuning.MaxAnkleTurn, tuning.MaxAnkleRoll);
    }

    private void StandOnGround(bool onFeet, float floor, LayerMask ground, in Tuning tuning, float dt, in Stride stride, in Hold hold)
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
            Left.Kept = Right.Kept = Left.Stepping = Right.Stepping = false;
            Left.StepLift = Right.StepLift = 0f;
            Left.Off = Right.Off = Vector3.zero;
            return;
        }
        if (onFeet)
        {
            float follow = settled ? 1f - Mathf.Exp(-dt / tuning.SettleTime) : 1f;
            Probe(Left, floor, ground, follow, tuning, dt);
            Probe(Right, floor, ground, follow, tuning, dt);
            // A missing support fades toward the clip instead of snapping a leg or the hips.
            float lowest = Mathf.Min(Mathf.Lerp(floor, Left.Ground, Left.SupportWeight),
                Mathf.Lerp(floor, Right.Ground, Right.SupportWeight));
            lowest = Mathf.Clamp(lowest, floor - tuning.MaxHipDrop, floor);
            hipFloor = Mathf.Clamp(Mathf.Lerp(hipFloor, lowest, follow), floor - tuning.MaxHipDrop, floor);
            settled = true;
        }
        else
        {
            // Leaving the ground: hold the last heights relative to the capsule while the weight fades.
            float moved = floor - lastFloor;
            hipFloor += moved;
            Left.Ground += moved;
            Right.Ground += moved;
        }
        lastFloor = floor;

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
        body.position += Vector3.up * ((hipFloor - clipFloor) * Weight);
        // Where each foot stands, level: the clip's place with the stride's stretch, the place it keeps, or where a step has
        // it (Hold). Turning to the right the right foot leads, to the left the left one.
        Vector3 leftAt, rightAt;
        if (Mathf.DeltaAngle(Left.Kept ? Left.KeptYaw : hold.Yaw, hold.Yaw) > 0f)
        {
            rightAt = Stand(Right, Left, rightLift, rightOver - lowerOver, onFeet, hold, dt);
            leftAt = Stand(Left, Right, leftLift, leftOver - lowerOver, onFeet, hold, dt);
        }
        else
        {
            leftAt = Stand(Left, Right, leftLift, leftOver - lowerOver, onFeet, hold, dt);
            rightAt = Stand(Right, Left, rightLift, rightOver - lowerOver, onFeet, hold, dt);
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
    private Vector3 Stand(Leg leg, Leg other, float lift, float over, bool onFeet, in Hold hold, float dt)
    {
        Vector3 clip = leg.Foot.position - hold.Carried + leg.Shift;
        bool keep = onFeet && hold.Stands && over < DownHeight && leg.SupportWeight > 0f && leg.Shift.sqrMagnitude < 1e-8f;
        leg.StepLift = 0f;
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
        else if (leg.Kept)
        {
            bool far = OutOfReach(leg, lift, leg.Place, hold.MaxDrop);
            if ((far || Mathf.Abs(Mathf.DeltaAngle(leg.KeptYaw, hold.Yaw)) > hold.Twist) && (!other.Stepping || far))
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
        Sample(leg.Foot.TransformPoint(leg.Heel) + leg.Shift);
        Sample(leg.Foot.position + leg.Shift);
        Sample(leg.Foot.TransformPoint(leg.Tip) + leg.Shift);
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

        // The first thing below the point that isn't the player itself, if it is something to stand on (not a wall's side).
        void Sample(Vector3 point)
        {
            int count = Physics.RaycastNonAlloc(new Vector3(point.x, floor + reach, point.z), Vector3.down, Hits, reach * 2f,
                ground, QueryTriggerInteraction.Ignore);
            int nearest = -1;
            for (int i = 0; i < count; i++)
                if ((nearest < 0 || Hits[i].distance < Hits[nearest].distance) && !Hits[i].collider.transform.IsChildOf(owner))
                    nearest = i;
            if (nearest < 0) return;
            var hit = Hits[nearest];
            if (hit.normal.y > .5f && (!found || hit.point.y > height))
            {
                height = hit.point.y;
                normal = hit.normal;
                surface = hit.collider;
                found = true;
            }
        }
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
