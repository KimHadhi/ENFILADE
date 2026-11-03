using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Plants the feet on the real ground, the way GMod's IK Foot addon does.
/// - Each foot looks for the ground under its heel, ankle and toes and stands on the highest, so no part of it sinks
///   into a step.
/// - Where one stands lower than the capsule's floor (a step down, a slope), the hips drop so it can reach; the other
///   knee bends to meet higher ground. Knees bend about the clip's own knee axis and the leg never turns about its
///   length, so the ankle does not twist (GMod's IK Foot only ever bends thigh and calf on their pitch axis).
/// - Each foot moves by the height of the ground under it, so the clip's own stride and foot lift stay as they are.
/// - Ground and hip heights are kept in world space, so the capsule popping up or down a step never drags a foot
///   through it. Higher ground is taken at once; lower ground and the hips settle over footSettleTime.
/// - Planted feet tilt to the slope.
/// - In every state, each ankle's turn about the shin and roll to the side are held within maxAnkleTurn / maxAnkleRoll
///   of the rest pose (AnkleLimit), so no clip can twist a foot.
/// The ground-following part is on while standing or crouching on the ground,
/// landings included, before the eyes place the camera, so the view
/// dips with the hips. Presentation only: nothing here moves the player.
/// The feet after the IK are read by PlayerFootsteps (TryReadFeet), so steps sound when a foot really comes down.
/// </summary>
public sealed partial class PlayerBody
{
    /// <summary>One foot after this frame's leg IK, for footstep sounds.</summary>
    internal readonly struct FootReading
    {
        /// <summary>The ankle (the foot bone) in world space.</summary>
        public readonly Vector3 Ankle;
        /// <summary>World height of the ground under the foot (m).</summary>
        public readonly float Ground;
        /// <summary>What that ground is; null when the foot has no support.</summary>
        public readonly Collider Surface;
        public bool HasSupport => Surface != null;

        public FootReading(Vector3 ankle, float ground, Collider surface) { Ankle = ankle; Ground = ground; Surface = surface; }

        /// <summary>How far the ankle is above the ground under it (m).</summary>
        public float Height => Ankle.y - Ground;
        public Vector3 GroundPoint => new Vector3(Ankle.x, Ground, Ankle.z);
    }

    [Header("Feet on the ground")]
    [SerializeField, Min(0f), Tooltip("Furthest the hips drop to let a foot reach lower ground (m).")]
    private float maxHipDrop = .3f;
    [SerializeField, Min(.05f), Tooltip("How far above and below the capsule's floor each foot looks for ground (m).")]
    private float footReach = .5f;
    [SerializeField, Min(.01f), Tooltip("How quickly the hips and feet settle onto lower ground (s). Higher ground is taken at once.")]
    private float footSettleTime = .08f;
    [SerializeField, Range(0f, 60f), Tooltip("Most a planted foot tilts to match a slope (degrees).")]
    private float maxFootTilt = 30f;
    [SerializeField, Range(0f, 180f), Tooltip("Most a foot turns about the shin, either way, in any clip (degrees).")]
    private float maxAnkleTurn = 20f;
    [SerializeField, Range(0f, 180f), Tooltip("Most a foot rolls onto its side, either way, in any clip (degrees).")]
    private float maxAnkleRoll = 25f;

    private sealed class Leg
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

    private Leg leftLeg, rightLeg;
    private float hipFloor, lastFloor, feetWeight;
    private bool feetSettled, feetOnGround;

    /// <summary>The feet after this frame's leg IK, on the ground.</summary>
    internal bool TryReadFeet(out FootReading left, out FootReading right)
    {
        bool reading = feetOnGround && leftLeg != null && rightLeg != null && body != null;
        left = reading ? new FootReading(leftLeg.Foot.position, leftLeg.Ground, leftLeg.Surface) : default;
        right = reading ? new FootReading(rightLeg.Foot.position, rightLeg.Ground, rightLeg.Surface) : default;
        return reading;
    }

    private void SetUpFeet()
    {
        leftLeg = MakeLeg(true);
        rightLeg = MakeLeg(false);
        feetWeight = 0f;
        feetSettled = feetOnGround = false;
        if (leftLeg == null || rightLeg == null) return;
        Vector3 hips = rightLeg.Upper.position - leftLeg.Upper.position;
        leftLeg.Ankle = AnkleLimit.Create(leftLeg.Foot, hips);
        rightLeg.Ankle = AnkleLimit.Create(rightLeg.Foot, hips);
    }

    private Leg MakeLeg(bool left)
    {
        if (!animator.isHuman) return null;
        Transform Bone(HumanBodyBones l, HumanBodyBones r) => animator.GetBoneTransform(left ? l : r);
        var upper = Bone(HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg);
        var lower = Bone(HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg);
        var foot = Bone(HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot);
        if (upper == null || lower == null || foot == null) return null;
        // Measured in the rest pose: the ball of the foot is the toe joint, or about twice the ankle's height ahead.
        var toes = Bone(HumanBodyBones.LeftToes, HumanBodyBones.RightToes);
        Vector3 ankle = foot.position, forward = restForward.sqrMagnitude > .5f ? restForward : body.forward;
        Vector3 ball = toes != null ? toes.position : ankle + forward * 2f * Mathf.Max(.04f, ankle.y - body.position.y);
        Vector3 along = Vector3.ProjectOnPlane(ball - ankle, Vector3.up);
        if (along.sqrMagnitude < 1e-4f) along = Vector3.ProjectOnPlane(forward, Vector3.up).normalized * .12f;
        // The toe tip reaches as far as the toe bones do (some rigs end in a toe-end bone), past the ball either way.
        float ballDistance = along.magnitude, tipDistance = ballDistance * 1.4f;
        if (toes != null)
            foreach (var bone in toes.GetComponentsInChildren<Transform>())
                tipDistance = Mathf.Max(tipDistance, Vector3.Dot(bone.position - ankle, along / ballDistance));
        tipDistance = Mathf.Min(tipDistance, ballDistance * 2.5f);
        Vector3 floor = Vector3.down * (ankle.y - body.position.y);
        return new Leg
        {
            Upper = upper, Lower = lower, Foot = foot, Sole = ankle.y - body.position.y,
            Heel = foot.InverseTransformPoint(ankle - along * .35f), Tip = foot.InverseTransformPoint(ankle + along * 1.4f),
            HeelSole = foot.InverseTransformPoint(ankle - along * .35f + floor),
            TipSole = foot.InverseTransformPoint(ankle + along / ballDistance * tipDistance + floor)
        };
    }

    private void PlantFeet(float dt)
    {
        if (leftLeg == null || rightLeg == null) return;
        // In every state, whatever the clip or the IK does, an ankle stays within what a real one can do: held once
        // so a planted foot is levelled from a sane foot, and again after the legs have bent.
        HoldAnkles();
        StandOnGround(dt);
        HoldAnkles();
    }

    private void HoldAnkles()
    {
        leftLeg.Ankle?.Apply(maxAnkleTurn, maxAnkleRoll);
        rightLeg.Ankle?.Apply(maxAnkleTurn, maxAnkleRoll);
    }

    private void StandOnGround(float dt)
    {
        bool onFeet = !dead && !playingOut && !heaving && player.IsGrounded
            && (state == MovementState.Standing || state == MovementState.Crouching);
        feetOnGround = onFeet;
        feetWeight = Mathf.MoveTowards(feetWeight, onFeet ? 1f : 0f, dt / blendTime);
        float floor = transform.position.y;
        float clipFloor = body.position.y;
        if (feetWeight <= 0f)
        {
            feetSettled = false;
            return;
        }
        if (onFeet)
        {
            float follow = feetSettled ? 1f - Mathf.Exp(-dt / footSettleTime) : 1f;
            Probe(leftLeg, floor, follow, dt);
            Probe(rightLeg, floor, follow, dt);
            // A missing support fades toward the clip instead of snapping a leg or the hips.
            float lowest = Mathf.Min(Mathf.Lerp(floor, leftLeg.Ground, leftLeg.SupportWeight),
                Mathf.Lerp(floor, rightLeg.Ground, rightLeg.SupportWeight));
            lowest = Mathf.Clamp(lowest, floor - maxHipDrop, floor);
            hipFloor = Mathf.Clamp(Mathf.Lerp(hipFloor, lowest, follow), floor - maxHipDrop, floor);
            feetSettled = true;
        }
        else
        {
            // Leaving the ground: hold the last heights relative to the capsule while the weight fades.
            float moved = floor - lastFloor;
            hipFloor += moved;
            leftLeg.Ground += moved;
            rightLeg.Ground += moved;
        }
        lastFloor = floor;

        // A clip made for other proportions can put a foot through the floor: a deep crouch holds the ankle lower than the
        // sole lets it stand, and pointing the toes down drives a longer foot's toe tip in. No part of the sole comes
        // below the ground; the ankle stands that much higher, and the knee bends for it.
        float leftLift = Mathf.Max(leftLeg.Foot.position.y - clipFloor, SoleDepth(leftLeg));
        float rightLift = Mathf.Max(rightLeg.Foot.position.y - clipFloor, SoleDepth(rightLeg));
        float lowerLift = Mathf.Min(leftLift, rightLift);
        body.position += Vector3.up * ((hipFloor - clipFloor) * feetWeight);
        Vector3 hips = (rightLeg.Upper.position - leftLeg.Upper.position).normalized;
        Place(leftLeg, leftLift, leftLift - lowerLift, hips);
        Place(rightLeg, rightLift, rightLift - lowerLift, hips);
    }

    // How high the ankle has to stand for the heel and the toe tip to stay on or above the ground, the foot turned as the
    // clip has it. Flat as in the rest pose, it is the ankle's own height over the sole.
    private static float SoleDepth(Leg leg)
    {
        float ankle = leg.Foot.position.y;
        return Mathf.Max(leg.Sole, ankle - leg.Foot.TransformPoint(leg.HeelSole).y, ankle - leg.Foot.TransformPoint(leg.TipSole).y);
    }

    private void Probe(Leg leg, float floor, float follow, float dt)
    {
        float ground = floor;
        Vector3 normal = Vector3.up;
        Collider surface = null;
        bool found = false;
        Sample(leg.Foot.TransformPoint(leg.Heel));
        Sample(leg.Foot.position);
        Sample(leg.Foot.TransformPoint(leg.Tip));
        float previousWeight = leg.SupportWeight;
        leg.SupportWeight = Mathf.MoveTowards(previousWeight, found ? 1f : 0f, dt / Mathf.Max(.01f, footSettleTime));
        if (!found)
        {
            leg.Surface = null;
            // Keep the last target relative to the capsule while its influence fades.
            leg.Ground += floor - lastFloor;
            return;
        }
        leg.Ground = previousWeight <= 0f || ground > leg.Ground ? ground : Mathf.Lerp(leg.Ground, ground, follow);
        leg.Normal = previousWeight <= 0f ? normal : Vector3.Slerp(leg.Normal, normal, follow);
        leg.Surface = surface;

        void Sample(Vector3 point)
        {
            if (Physics.Raycast(new Vector3(point.x, floor + footReach, point.z), Vector3.down, out RaycastHit hit, footReach * 2f,
                    player.groundLayers, QueryTriggerInteraction.Ignore)
                && hit.normal.y > .5f && (!found || hit.point.y > ground))
            {
                ground = hit.point.y;
                normal = hit.normal;
                surface = hit.collider;
                found = true;
            }
        }
    }

    // lift: how high the clip holds this ankle above its own floor. above: how far over the clip's lower foot.
    // hips: from the left hip joint to the right one.
    private void Place(Leg leg, float lift, float above, Vector3 hips)
    {
        Vector3 foot = leg.Foot.position;
        float weight = feetWeight * leg.SupportWeight;
        if (weight <= 0f)
        {
            leg.Target = foot;
            return;
        }
        Quaternion rotation = leg.Foot.rotation;
        // A Q/E lean has moved the hips over (LeanHips); the feet stay where they stood.
        Vector3 stand = foot - hipLean * weight;
        leg.Target = new Vector3(stand.x, Mathf.Lerp(foot.y, leg.Ground + lift, weight), stand.z);
        // The knee bends about the clip's own knee axis. A nearly straight leg has no clear axis, so it bends
        // forward about the hips' sideways line instead (a clip knee bent slightly backward counts as straight).
        Vector3 thigh = leg.Lower.position - leg.Upper.position, shin = foot - leg.Lower.position;
        Vector3 own = Vector3.Cross(thigh, shin), straight = Vector3.ProjectOnPlane(hips, thigh).normalized;
        float bent = Mathf.Clamp01(own.magnitude / Mathf.Max(1e-6f, thigh.magnitude * shin.magnitude) / .26f);
        if (Vector3.Dot(own, straight) < 0f && bent < 1f) bent = 0f;
        TwoBoneIK.Reach(leg.Upper, leg.Lower, leg.Foot, leg.Target, Vector3.Lerp(straight, own.normalized, bent));
        // A foot on the ground lies along the slope; a swinging one keeps the clip's angle.
        float planted = (1f - Mathf.Clamp01(above / .1f)) * weight;
        Quaternion tilt = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, leg.Normal), maxFootTilt);
        leg.Foot.rotation = Quaternion.Slerp(rotation, tilt * rotation, planted);
    }

    // Select the player in the Scene view: the line is the ground each foot found, the sphere where its ankle goes.
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || feetWeight <= 0f) return;
        Draw(leftLeg);
        Draw(rightLeg);

        void Draw(Leg leg)
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
