using System;
using System.Collections.Generic;
using MagicaCloth2;
using UnityEngine;

/// <summary>
/// What cloth can't pass through. Magica Cloth doesn't see Unity's colliders, so these are its own, measured from the
/// avatar as the body is made, so any humanoid gets them in its own size. They stop cloth and nothing else: combat never
/// reads them. Only the PC that simulates the cloth makes them. Numbers: Tables/ClothBody.
/// Arms push a skirt only outward: the skirt's backstop has the last word, so a hand can't press it into the legs, where
/// it would catch the way a raised thigh once caught the front of one.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerBodyClothColliders : MonoBehaviour
{
    private const string Legs = "legs", Arms = "arms", Head = "head";
    private const char ShapeSeparator = '|';

    [Header("Legs")]
    [SerializeField, Range(0f, 1f), Tooltip("Thigh radius at the hip and at the knee, as a share of its length.")]
    private float thighStart = .17f, thighEnd = .165f;
    [SerializeField, Range(0f, 1f), Tooltip("Shin radius at the knee and at the ankle, as a share of its length.")]
    private float shinStart = .15f, shinEnd = .13f;
    [SerializeField, Range(0f, 2f), Tooltip("Pelvis radius, as a share of the distance between the hip joints.")]
    private float pelvis = .7f;

    [Header("Arms")]
    [SerializeField, Range(0f, 1f), Tooltip("Upper arm radius at the shoulder and at the elbow, as a share of its length.")]
    private float upperArmStart = .14f, upperArmEnd = .11f;
    [SerializeField, Range(0f, 1f), Tooltip("Forearm radius at the elbow and at the wrist, as a share of its length.")]
    private float forearmStart = .12f, forearmEnd = .1f;
    [SerializeField, Range(0f, 2f), Tooltip("Palm radius, as a share of the distance from the wrist to the middle finger's knuckle.")]
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
    private CharacterController capsule;
    private PlayerMovement player;
    private Animator madeFor;
    private MagicaPlaneCollider floor;

    /// <summary>
    /// The shapes a part of the cloth touches on this body ("legs|arms"), each set made the first time it is asked for,
    /// so two parts that touch the legs share one set.
    /// </summary>
    internal List<ColliderComponent> For(Animator animator, string shapes)
    {
        if (animator != madeFor) { made.Clear(); madeFor = animator; }
        var found = new List<ColliderComponent>();
        foreach (string shape in shapes.Split(ShapeSeparator)) found.AddRange(Set(animator, shape.Trim()));
        return found;
    }

    /// <summary>
    /// How far a point is from the nearest surface, among these shapes, that a cloth rests against. The arms don't count:
    /// they move and are meant to push it. Nor does the floor, which is off while standing. Infinity when nothing counts.
    /// </summary>
    internal float Room(Vector3 point, List<ColliderComponent> shapes)
    {
        made.TryGetValue(Arms, out var arms);
        float least = float.PositiveInfinity;
        foreach (var shape in shapes)
        {
            if (shape == null || shape is MagicaPlaneCollider || (arms != null && arms.Contains(shape))) continue;
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

    private List<ColliderComponent> Set(Animator animator, string shape)
    {
        if (made.TryGetValue(shape, out var set)) return set;
        set = new List<ColliderComponent>();
        if (string.Equals(shape, Legs, StringComparison.OrdinalIgnoreCase)) MakeLegs(animator, set);
        else if (string.Equals(shape, Arms, StringComparison.OrdinalIgnoreCase)) MakeArms(animator, set);
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

    private void MakeArms(Animator animator, List<ColliderComponent> into)
    {
        Arm(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleProximal, into);
        Arm(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.RightMiddleProximal, into);
    }

    private void Arm(Animator animator, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand, HumanBodyBones knuckle,
        List<ColliderComponent> into)
    {
        Transform shoulder = animator.GetBoneTransform(upper), elbow = animator.GetBoneTransform(lower);
        Transform wrist = animator.GetBoneTransform(hand), finger = animator.GetBoneTransform(knuckle);
        Limb(shoulder, elbow, upperArmStart, upperArmEnd, into);
        Limb(elbow, wrist, forearmStart, forearmEnd, into);
        // A rig without finger bones keeps its forearm and loses only the palm.
        if (finger == null) return;
        float width = Vector3.Distance(wrist.position, finger.position) * palm;
        Capsule(wrist, finger, width, width, into);
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
        floor.enabled = LowOnTheGround;
        floor.transform.position = new Vector3(transform.position.x, capsule.bounds.min.y, transform.position.z);
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
