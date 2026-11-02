using System;
using System.Collections.Generic;
using MagicaCloth2;
using UnityEngine;

/// <summary>
/// What cloth can't pass through. Magica Cloth doesn't see Unity's colliders, so these are its own, measured from the
/// avatar as the body is made, so any humanoid gets them in its own size. They stop cloth and nothing else: combat never
/// reads them. Only the PC that simulates the cloth makes them. Numbers: Tables/ClothBody.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerBodyClothColliders : MonoBehaviour
{
    private const string Legs = "legs", Head = "head";

    [Header("Legs")]
    [SerializeField, Range(0f, 1f), Tooltip("Thigh radius at the hip and at the knee, as a share of its length.")]
    private float thighStart = .17f, thighEnd = .165f;
    [SerializeField, Range(0f, 1f), Tooltip("Shin radius at the knee and at the ankle, as a share of its length.")]
    private float shinStart = .15f, shinEnd = .13f;
    [SerializeField, Range(0f, 2f), Tooltip("Pelvis radius, as a share of the distance between the hip joints.")]
    private float pelvis = .7f;

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

    /// <summary>The shapes a part of the cloth touches on this body, made the first time they are asked for.</summary>
    internal List<ColliderComponent> For(Animator animator, string shapes)
    {
        if (animator != madeFor) { made.Clear(); madeFor = animator; }
        if (made.TryGetValue(shapes, out var found)) return found;
        found = new List<ColliderComponent>();
        if (string.Equals(shapes, Legs, StringComparison.OrdinalIgnoreCase)) MakeLegs(animator, found);
        else if (string.Equals(shapes, Head, StringComparison.OrdinalIgnoreCase)) MakeHead(animator, found);
        return made[shapes] = found;
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
