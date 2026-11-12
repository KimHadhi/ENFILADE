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

    internal sealed class Leg
    {
        public Transform Upper, Lower, Foot;
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
    private float hipFloor, lastFloor;
    private bool settled;

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
    /// After the clips have posed the legs: the ankles held within their limits, then, while <paramref name="onFeet"/>,
    /// the feet on the ground under them and the hips dropped for the lower one. <paramref name="floor"/> is the capsule's
    /// floor (world height).
    /// </summary>
    public void Plant(bool onFeet, float floor, LayerMask ground, in Tuning tuning, float dt)
    {
        // In every state, whatever the clip or the IK does, an ankle stays within what a real one can do: held once so a
        // planted foot is levelled from a sane foot, and again after the legs have bent.
        HoldAnkles(tuning);
        StandOnGround(onFeet, floor, ground, tuning, dt);
        HoldAnkles(tuning);
    }

    private void HoldAnkles(in Tuning tuning)
    {
        Left.Ankle?.Apply(tuning.MaxAnkleTurn, tuning.MaxAnkleRoll);
        Right.Ankle?.Apply(tuning.MaxAnkleTurn, tuning.MaxAnkleRoll);
    }

    private void StandOnGround(bool onFeet, float floor, LayerMask ground, in Tuning tuning, float dt)
    {
        OnGround = onFeet;
        Weight = Mathf.MoveTowards(Weight, onFeet ? 1f : 0f, dt / tuning.BlendTime);
        // The floor the clip stands the soles on: the root, and the drawn soles over it (Measure).
        float clipFloor = body.position.y + sole;
        if (Weight <= 0f)
        {
            settled = false;
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
        float leftLift = Mathf.Max(Left.Foot.position.y - clipFloor, SoleDepth(Left));
        float rightLift = Mathf.Max(Right.Foot.position.y - clipFloor, SoleDepth(Right));
        float lowerLift = Mathf.Min(leftLift, rightLift);
        body.position += Vector3.up * ((hipFloor - clipFloor) * Weight);
        Vector3 hips = (Right.Upper.position - Left.Upper.position).normalized;
        Place(Left, leftLift, leftLift - lowerLift, hips, tuning);
        Place(Right, rightLift, rightLift - lowerLift, hips, tuning);
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
        Sample(leg.Foot.TransformPoint(leg.Heel));
        Sample(leg.Foot.position);
        Sample(leg.Foot.TransformPoint(leg.Tip));
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
    // hips: from the left hip joint to the right one.
    private void Place(Leg leg, float lift, float above, Vector3 hips, in Tuning tuning)
    {
        Vector3 foot = leg.Foot.position;
        float weight = Weight * leg.SupportWeight;
        if (weight <= 0f)
        {
            leg.Target = foot;
            return;
        }
        Quaternion rotation = leg.Foot.rotation;
        leg.Target = new Vector3(foot.x, Mathf.Lerp(foot.y, leg.Ground + lift, weight), foot.z);
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
