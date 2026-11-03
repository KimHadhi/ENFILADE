using System;
using System.Collections.Generic;
using MagicaCloth2;
using UnityEngine;

/// <summary>
/// What cloth can't pass through. Magica Cloth doesn't see Unity's colliders, so these are its own, measured from the
/// avatar as the body is made, so any humanoid gets them in its own size. They stop cloth and nothing else: combat never
/// reads them. The PC that simulates the cloth makes them. A copy makes the legs only when a pressed skirt has no measured
/// skin to stop at (BodySurface), to know where they are; nothing collides with them there. Numbers: Tables/ClothBody.
/// The arms aren't colliders. A collider pushes cloth out of itself every way, so a hand the clip puts inside a skirt
/// would swell it like a fist in a bag. They press skirts in once the cloth has moved instead (SkirtPress), measured here
/// the same way.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerBodyClothColliders : MonoBehaviour
{
    private const string Legs = "legs", Arms = "arms", Head = "head";
    private const char ShapeSeparator = '|';

    /// <summary>One bone of an arm as a skirt meets it: a rounded rod from one joint to the next.</summary>
    internal readonly struct ArmBone
    {
        private readonly Transform from, to;
        private readonly float start, end;

        /// <summary>Which arm it is part of: 0 left, 1 right.</summary>
        internal int Side { get; }

        internal ArmBone(Transform from, Transform to, float start, float end, int side)
        {
            this.from = from;
            this.to = to;
            this.start = start;
            this.end = end;
            Side = side;
        }

        /// <summary>The point of the bone's middle line nearest to a point, and how thick the arm is there (metres).</summary>
        internal Vector3 Nearest(Vector3 point, out float radius)
        {
            Vector3 origin = from.position, line = to.position - origin;
            float along = line.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(point - origin, line) / line.sqrMagnitude) : 0f;
            radius = Mathf.Lerp(start, end, along);
            return origin + line * along;
        }
    }

    [Header("Legs")]
    [SerializeField, Range(0f, 1f), Tooltip("Thigh radius at the hip and at the knee, as a share of its length.")]
    private float thighStart = .17f, thighEnd = .165f;
    [SerializeField, Range(0f, 1f), Tooltip("Shin radius at the knee and at the ankle, as a share of its length.")]
    private float shinStart = .15f, shinEnd = .13f;
    [SerializeField, Range(0f, 2f), Tooltip("Pelvis radius, as a share of the distance between the hip joints.")]
    private float pelvis = .7f;

    [Header("Arms, pressing a skirt")]
    [SerializeField, Range(0f, 1f), Tooltip("Upper arm radius at the shoulder and at the elbow, as a share of its length.")]
    private float upperArmStart = .14f, upperArmEnd = .11f;
    [SerializeField, Range(0f, 1f), Tooltip("Forearm radius at the elbow and at the wrist, as a share of its length.")]
    private float forearmStart = .12f, forearmEnd = .1f;
    [SerializeField, Range(0f, 2f), Tooltip("Hand radius, as a share of the distance from the wrist to the middle finger's knuckle. The hand runs on to the finger's middle joint.")]
    private float palm = .45f;

    [Header("Upper body")]
    [SerializeField, Range(0f, 2f), Tooltip("How far the head's centre sits above the head bone, as a share of the shoulder width.")]
    private float headLift = .5f;
    [SerializeField, Range(0f, 2f), Tooltip("Head radius, as a share of the shoulder width.")]
    private float headRadius = .39f;
    [SerializeField, Range(0f, 2f), Tooltip("Chest radius, as a share of the shoulder width.")]
    private float chest = .35f;

    private readonly Dictionary<string, List<ColliderComponent>> made =
        new Dictionary<string, List<ColliderComponent>>(StringComparer.OrdinalIgnoreCase);
    private ArmBone[] arms;
    private CharacterController capsule;
    private PlayerMovement player;
    private Animator madeFor;
    private MagicaPlaneCollider floor;

    /// <summary>Whether a part's shapes ("legs|arms") let the arms press it.</summary>
    internal static bool PressedByArms(string shapes)
    {
        foreach (string shape in shapes.Split(ShapeSeparator))
            if (string.Equals(shape.Trim(), Arms, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// The colliders a part of the cloth touches on this body ("legs|arms"), each set made the first time it is asked
    /// for, so two parts that touch the legs share one set. The arms make none: they press instead.
    /// </summary>
    internal List<ColliderComponent> For(Animator animator, string shapes)
    {
        Use(animator);
        var found = new List<ColliderComponent>();
        foreach (string shape in shapes.Split(ShapeSeparator))
        {
            string name = shape.Trim();
            if (!string.Equals(name, Arms, StringComparison.OrdinalIgnoreCase)) found.AddRange(Set(animator, name));
        }
        return found;
    }

    /// <summary>Both arms' bones, upper arm to fingers, as they press a skirt.</summary>
    internal ArmBone[] ArmBones(Animator animator)
    {
        Use(animator);
        if (arms != null) return arms;
        var list = new List<ArmBone>();
        Arm(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, 0, list);
        Arm(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, 1, list);
        return arms = list.ToArray();
    }

    /// <summary>
    /// How far a point is from the nearest surface, among these colliders, that a cloth rests against. The floor doesn't
    /// count: it is off while standing. Infinity when nothing counts.
    /// </summary>
    internal float Room(Vector3 point, List<ColliderComponent> shapes)
    {
        float least = float.PositiveInfinity;
        foreach (var shape in shapes)
        {
            if (shape == null || shape is MagicaPlaneCollider) continue;
            float scale = Mathf.Abs(shape.GetScale());
            Vector3 size = shape.GetSize() * scale, start = shape.transform.TransformPoint(shape.center);
            if (shape is MagicaCapsuleCollider)
            {
                // As Capsule makes it: down the object's -Y, the length counting both ends' rounding.
                Vector3 axis = -shape.transform.up * Mathf.Max(0f, size.z - size.x - size.y);
                float along = axis.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(point - start, axis) / axis.sqrMagnitude) : 0f;
                least = Mathf.Min(least, Vector3.Distance(point, start + axis * along) - Mathf.Lerp(size.x, size.y, along));
            }
            else least = Mathf.Min(least, Vector3.Distance(point, start) - size.x);
        }
        return least;
    }

    // Everything made is for one body: a new one starts over, the old one's colliders gone with it.
    private void Use(Animator animator)
    {
        if (animator == madeFor) return;
        made.Clear();
        arms = null;
        madeFor = animator;
    }

    private List<ColliderComponent> Set(Animator animator, string shape)
    {
        if (made.TryGetValue(shape, out var set)) return set;
        set = new List<ColliderComponent>();
        if (string.Equals(shape, Legs, StringComparison.OrdinalIgnoreCase)) MakeLegs(animator, set);
        else if (string.Equals(shape, Head, StringComparison.OrdinalIgnoreCase)) MakeHead(animator, set);
        return made[shape] = set;
    }

    private void MakeLegs(Animator animator, List<ColliderComponent> into)
    {
        Transform leftLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg), rightLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        Transform leftKnee = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg), rightKnee = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        Ball(animator.GetBoneTransform(HumanBodyBones.Hips), (leftLeg.position + rightLeg.position) * .5f,
            Vector3.Distance(leftLeg.position, rightLeg.position) * pelvis, into);
        Limb(leftLeg, leftKnee, thighStart, thighEnd, into);
        Limb(rightLeg, rightKnee, thighStart, thighEnd, into);
        Limb(leftKnee, animator.GetBoneTransform(HumanBodyBones.LeftFoot), shinStart, shinEnd, into);
        Limb(rightKnee, animator.GetBoneTransform(HumanBodyBones.RightFoot), shinStart, shinEnd, into);
        into.Add(MakeFloor());
    }

    private void Arm(Animator animator, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand, HumanBodyBones knuckle,
        HumanBodyBones fingerMiddle, int side, List<ArmBone> into)
    {
        Transform shoulder = animator.GetBoneTransform(upper), elbow = animator.GetBoneTransform(lower);
        Transform wrist = animator.GetBoneTransform(hand), finger = animator.GetBoneTransform(knuckle);
        if (shoulder == null || elbow == null || wrist == null) return;
        Span(shoulder, elbow, upperArmStart, upperArmEnd, side, into);
        Span(elbow, wrist, forearmStart, forearmEnd, side, into);
        // A rig without finger bones keeps its forearm and loses only the hand. A hand hanging down reaches a skirt with
        // its fingers, so it runs past the knuckles; a fist curls that joint back in.
        if (finger == null) return;
        float width = Vector3.Distance(wrist.position, finger.position) * palm;
        into.Add(new ArmBone(wrist, animator.GetBoneTransform(fingerMiddle) ?? finger, width, width, side));
    }

    private static void Span(Transform from, Transform to, float start, float end, int side, List<ArmBone> into)
    {
        float length = Vector3.Distance(from.position, to.position);
        into.Add(new ArmBone(from, to, length * start, length * end, side));
    }

    private void MakeHead(Animator animator, List<ColliderComponent> into)
    {
        Transform head = animator.GetBoneTransform(HumanBodyBones.Head), neck = animator.GetBoneTransform(HumanBodyBones.Neck) ?? head;
        Transform upperBody = animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
        float shoulders = Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position,
            animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position);
        Vector3 up = (head.position - neck.position).normalized;
        Ball(head, head.position + up * (shoulders * headLift), shoulders * headRadius, into);
        Capsule(upperBody, neck, shoulders * chest, shoulders * chest, into);
    }

    // The floor is only a plane under the capsule, which keeps a slide's cloth out of the ground.
    private MagicaPlaneCollider MakeFloor()
    {
        if (floor == null)
        {
            floor = new GameObject("Cloth floor", typeof(MagicaPlaneCollider)).GetComponent<MagicaPlaneCollider>();
            floor.transform.SetParent(transform, false);
            capsule = GetComponent<CharacterController>();
            player = GetComponent<PlayerMovement>();
        }
        return floor;
    }

    private void LateUpdate()
    {
        if (floor == null) return;
        if (player.IsDead && FloorUnderBody()) return;
        floor.enabled = LowOnTheGround;
        floor.transform.SetPositionAndRotation(new Vector3(transform.position.x, capsule.bounds.min.y, transform.position.z), transform.rotation);
    }

    // A corpse lies wherever it fell or was knocked to, while the capsule stays where the player died, so the floor goes
    // on the ground under the body's hips, along its slope. Bodies are on the players' layer, which isn't ground, so the
    // ray finds the ground under it rather than the body itself.
    private bool FloorUnderBody()
    {
        const float above = .25f, below = 2f;
        Transform hips = madeFor != null ? madeFor.GetBoneTransform(HumanBodyBones.Hips) : null;
        if (hips == null || !Physics.Raycast(hips.position + Vector3.up * above, Vector3.down, out RaycastHit ground, above + below,
                player.groundLayers, QueryTriggerInteraction.Ignore)) return false;
        floor.enabled = true;
        floor.transform.SetPositionAndRotation(ground.point, Quaternion.FromToRotation(Vector3.up, ground.normal));
        return true;
    }

    // Standing, cloth doesn't reach the floor. Going over something, the capsule's bottom is a plane in the air, and a plane
    // that high throws the skirt up and over.
    private bool LowOnTheGround => player.IsGrounded && player.CurrentState is PlayerMovement.MovementState.Crouching
        or PlayerMovement.MovementState.Sliding or PlayerMovement.MovementState.HeadFirstSliding
        or PlayerMovement.MovementState.SupineSliding or PlayerMovement.MovementState.Proning
        or PlayerMovement.MovementState.Supine or PlayerMovement.MovementState.LandingRoll;

    private void Limb(Transform from, Transform to, float start, float end, List<ColliderComponent> into)
    {
        float length = Vector3.Distance(from.position, to.position);
        Capsule(from, to, length * start, length * end, into);
    }

    // Aligned to its start, Magica's capsule runs down the object's -Y, and its length counts both ends' rounding.
    private static void Capsule(Transform from, Transform to, float start, float end, List<ColliderComponent> into)
    {
        var shape = Shape<MagicaCapsuleCollider>(from, from.position, Quaternion.FromToRotation(Vector3.down, to.position - from.position));
        float scale = Mathf.Abs(shape.GetScale());
        shape.direction = MagicaCapsuleCollider.Direction.Y;
        shape.alignedOnCenter = false;
        shape.SetSize(start / scale, end / scale, (Vector3.Distance(from.position, to.position) + start + end) / scale);
        into.Add(shape);
    }

    private static void Ball(Transform bone, Vector3 centre, float radius, List<ColliderComponent> into)
    {
        var shape = Shape<MagicaSphereCollider>(bone, centre, Quaternion.identity);
        shape.SetSize(radius / Mathf.Abs(shape.GetScale()));
        into.Add(shape);
    }

    // A child of its own, because rigs roll their bones every way and a capsule only lines up with its object's axes.
    private static T Shape<T>(Transform bone, Vector3 position, Quaternion rotation) where T : ColliderComponent
    {
        var shape = new GameObject("Cloth collider", typeof(T));
        shape.transform.SetParent(bone, false);
        shape.transform.SetPositionAndRotation(position, rotation);
        return shape.GetComponent<T>();
    }
}
