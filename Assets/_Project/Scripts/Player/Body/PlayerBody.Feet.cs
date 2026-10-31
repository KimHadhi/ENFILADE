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
/// The ground-following part is on while standing or crouching on the ground, and while supported by a balance beam,
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
        /// <summary>What that ground is; null when nothing was found and the capsule's floor stood in.</summary>
        public readonly Collider Surface;

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
        /// <summary>Heel and toe tip in the foot bone's space; only their place over the ground is used.</summary>
        public Vector3 Heel, Tip;
        /// <summary>Smoothed world height of the ground under the foot (m).</summary>
        public float Ground;
        /// <summary>The collider under the foot, or null when the capsule's floor stood in.</summary>
        public Collider Surface;
        public Vector3 Normal = Vector3.up, Target, BeamPoint;
        public bool OnBeam;
    }

    private Leg leftLeg, rightLeg;
    private float hipFloor, lastFloor, feetWeight, restAnkleHeight;
    private bool feetSettled, feetOnGround;

    /// <summary>The ankles' height above the floor in the character's rest pose (m); 0 without legs.</summary>
    internal float RestAnkleHeight => restAnkleHeight;

    /// <summary>The feet after this frame's leg IK, on the ground or supported by a balance beam.</summary>
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
        feetWeight = restAnkleHeight = 0f;
        feetSettled = feetOnGround = false;
        if (leftLeg == null || rightLeg == null) return;
        Vector3 hips = rightLeg.Upper.position - leftLeg.Upper.position;
        leftLeg.Ankle = AnkleLimit.Create(leftLeg.Foot, hips);
        rightLeg.Ankle = AnkleLimit.Create(rightLeg.Foot, hips);
        restAnkleHeight = Mathf.Max(0f, (leftLeg.Foot.position.y + rightLeg.Foot.position.y) * .5f - body.position.y);
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
        return new Leg
        {
            Upper = upper, Lower = lower, Foot = foot,
            Heel = foot.InverseTransformPoint(ankle - along * .35f), Tip = foot.InverseTransformPoint(ankle + along * 1.4f)
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
        var fixtures = player.Abilities.Fixtures;
        var beam = fixtures.BalanceActive && fixtures.Beam != null && fixtures.Beam.isActiveAndEnabled ? fixtures.Beam : null;
        bool onFeet = !dead && !playingOut && (beam != null || (player.IsGrounded
            && (state == MovementState.Standing || state == MovementState.Crouching)));
        feetOnGround = onFeet;
        feetWeight = Mathf.MoveTowards(feetWeight, onFeet ? 1f : 0f, dt / blendTime);
        float floor = beam != null ? beam.PointAt(beam.DistanceOf(transform.position)).y : transform.position.y;
        float clipFloor = body.position.y;
        if (feetWeight <= 0f)
        {
            feetSettled = false;
            return;
        }
        if (onFeet)
        {
            float follow = feetSettled ? 1f - Mathf.Exp(-dt / footSettleTime) : 1f;
            Probe(leftLeg, floor, follow, beam);
            Probe(rightLeg, floor, follow, beam);
            // The hips carry the body's floor down to the lower foot's ground, within maxHipDrop under the capsule.
            float lowest = Mathf.Clamp(Mathf.Min(leftLeg.Ground, rightLeg.Ground), floor - maxHipDrop, floor);
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

        float leftLift = leftLeg.Foot.position.y - clipFloor, rightLift = rightLeg.Foot.position.y - clipFloor;
        float lowerLift = Mathf.Min(leftLift, rightLift);
        body.position += Vector3.up * ((hipFloor - clipFloor) * feetWeight);
        Vector3 hips = (rightLeg.Upper.position - leftLeg.Upper.position).normalized;
        Place(leftLeg, leftLift, leftLift - lowerLift, hips);
        Place(rightLeg, rightLift, rightLift - lowerLift, hips);
    }

    private void Probe(Leg leg, float floor, float follow, BalanceBeam beam)
    {
        leg.OnBeam = beam != null && beam.Surface is BoxCollider;
        if (leg.OnBeam)
        {
            var box = (BoxCollider)beam.Surface;
            Vector3 local = box.transform.InverseTransformPoint(leg.Foot.position);
            // Keep even a wide running stride on the top face while preserving the clip's vertical foot lift.
            local.x = Mathf.Clamp(local.x, box.center.x - box.size.x * .45f, box.center.x + box.size.x * .45f);
            local.z = Mathf.Clamp(local.z, box.center.z - box.size.z * .5f, box.center.z + box.size.z * .5f);
            local.y = box.center.y + box.size.y * .5f;
            leg.BeamPoint = box.transform.TransformPoint(local);
            leg.Ground = leg.BeamPoint.y;
            leg.Normal = box.transform.up;
            leg.Surface = box;
            return;
        }
        float ground = floor;
        Vector3 normal = Vector3.up;
        Collider surface = null;
        bool found = false;
        Sample(leg.Foot.TransformPoint(leg.Heel));
        Sample(leg.Foot.position);
        Sample(leg.Foot.TransformPoint(leg.Tip));
        leg.Ground = ground > leg.Ground ? ground : Mathf.Lerp(leg.Ground, ground, follow);
        leg.Normal = Vector3.Slerp(leg.Normal, normal, follow);
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
        Quaternion rotation = leg.Foot.rotation;
        leg.Target = new Vector3(foot.x, Mathf.Lerp(foot.y, leg.Ground + lift, feetWeight), foot.z);
        if (leg.OnBeam && feetOnGround)
        {
            leg.Target.x = Mathf.Lerp(foot.x, leg.BeamPoint.x, feetWeight);
            leg.Target.z = Mathf.Lerp(foot.z, leg.BeamPoint.z, feetWeight);
        }
        // The knee bends about the clip's own knee axis. A nearly straight leg has no clear axis, so it bends
        // forward about the hips' sideways line instead (a clip knee bent slightly backward counts as straight).
        Vector3 thigh = leg.Lower.position - leg.Upper.position, shin = foot - leg.Lower.position;
        Vector3 own = Vector3.Cross(thigh, shin), straight = Vector3.ProjectOnPlane(hips, thigh).normalized;
        float bent = Mathf.Clamp01(own.magnitude / Mathf.Max(1e-6f, thigh.magnitude * shin.magnitude) / .26f);
        if (Vector3.Dot(own, straight) < 0f && bent < 1f) bent = 0f;
        TwoBoneIK.Reach(leg.Upper, leg.Lower, leg.Foot, leg.Target, Vector3.Lerp(straight, own.normalized, bent));
        // A foot on the ground lies along the slope; a swinging one keeps the clip's angle.
        float planted = (1f - Mathf.Clamp01(above / .1f)) * feetWeight;
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
            Gizmos.color = Color.green;
            Gizmos.DrawLine(new Vector3(heel.x, leg.Ground, heel.z), new Vector3(tip.x, leg.Ground, tip.z));
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(leg.Target, .03f);
        }
    }
}
