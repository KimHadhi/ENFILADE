using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A limp copy of a humanoid body. Copying a character in the middle of a fight would hitch, so it is made once up front
/// and stowed unseen; dying only lays it out in the live model's pose and look. Whoever dies hides the original, so
/// nothing here can leave a living body broken. Simulated, it runs physics and can be pushed; otherwise it follows the
/// pose another PC's corpse reports (Capture, Follow), so every PC shows one body. The joints are made in the neutral
/// pose: limits are measured from the pose a joint is made in, and these should be a body's, not whatever pose it died
/// in. Twist limits read as Unity's ragdoll wizard does: negative bends a knee, an elbow or a nod forward.
/// </summary>
public sealed class RagdollCorpse : MonoBehaviour, IDamageReceiver
{
    // Kilos. Joints care how the parts weigh against each other: keep the heaviest within ten times the lightest.
    private const float HipsMass = 12f, ChestMass = 12f, HeadMass = 5f, ThighMass = 8f, ShinMass = 5f, UpperArmMass = 3f, ForearmMass = 2f;
    private const float ThighThickness = .22f, ShinThickness = .18f, UpperArmThickness = .2f, ForearmThickness = .16f;
    private const float Limp = .5f, Unsticking = 2f, FollowRate = 30f;
    private const int SolverSteps = 12;

    private readonly List<Rigidbody> parts = new List<Rigidbody>();
    private readonly List<Collider> shapes = new List<Collider>();
    private Transform[] liveBones, bones;
    private Renderer[] liveLooks, looks;
    private Rigidbody chest;
    private int layer;
    private LayerMask passes;

    public Transform Head { get; private set; }
    /// <summary>Bullets, blows and shoves that reach it. It takes no damage; whoever laid it decides how far they push it.</summary>
    public event Action<DamageHit> Struck;

    /// <summary>Makes a corpse of this live model and stows it. It touches only what's in solid; its shapes sit on this layer.</summary>
    public static RagdollCorpse Prepare(Animator live, int layer, LayerMask solid)
    {
        if (live == null || !live.isHuman) return null;
        var root = new GameObject("Corpse (" + live.name + ")");
        // Made under an inactive parent, the copy's scripts never wake.
        root.SetActive(false);
        var model = Instantiate(live.gameObject, live.transform.position, live.transform.rotation, root.transform);
        model.transform.localScale = live.transform.lossyScale;
        foreach (var script in model.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(script);
        DestroyImmediate(model.GetComponent<Animator>());
        var corpse = root.AddComponent<RagdollCorpse>();
        corpse.layer = layer;
        corpse.passes = ~solid;
        corpse.liveBones = live.GetComponentsInChildren<Transform>(true);
        corpse.bones = model.GetComponentsInChildren<Transform>(true);
        corpse.liveLooks = live.GetComponentsInChildren<Renderer>(true);
        corpse.looks = model.GetComponentsInChildren<Renderer>(true);
        root.SetActive(true);
        corpse.Build(live);
        corpse.Stow();
        return corpse;
    }

    /// <summary>Takes the live model's pose and look at this moment. Simulated, it drops; otherwise it waits to follow.</summary>
    public void Lay(bool simulated)
    {
        bones[0].SetPositionAndRotation(liveBones[0].position, liveBones[0].rotation);
        for (int i = 1; i < bones.Length; i++)
            if (liveBones[i] != null) bones[i].SetLocalPositionAndRotation(liveBones[i].localPosition, liveBones[i].localRotation);
        for (int i = 0; i < looks.Length; i++)
        {
            looks[i].enabled = true;
            if (liveLooks[i] != null) looks[i].shadowCastingMode = liveLooks[i].shadowCastingMode;
        }
        foreach (var shape in shapes) shape.enabled = true;
        Physics.SyncTransforms();
        foreach (var part in parts)
        {
            part.interpolation = simulated ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            part.isKinematic = !simulated;
        }
    }

    /// <summary>Out of sight and out of the world until the next death. The joints stay as made.</summary>
    public void Stow()
    {
        foreach (var part in parts)
        {
            if (part.isKinematic) continue;
            part.linearVelocity = Vector3.zero;
            part.angularVelocity = Vector3.zero;
            part.isKinematic = true;
        }
        foreach (var shape in shapes) shape.enabled = false;
        foreach (var look in looks) look.enabled = false;
    }

    DamageResult IDamageReceiver.ReceiveHit(DamageHit hit)
    {
        Struck?.Invoke(hit);
        return DamageResult.Ignored;
    }

    /// <summary>Lets this collider through, as a character brushes past a body rather than climbing it. Call it after Lay.</summary>
    public void PassThrough(Collider other)
    {
        foreach (var shape in shapes) Physics.IgnoreCollision(shape, other);
    }

    /// <summary>Starts every part moving, as a body keeps going when it drops.</summary>
    public void Launch(Vector3 velocity)
    {
        foreach (var part in parts) part.linearVelocity = velocity;
    }

    /// <summary>A blow to the upper body, which topples it over its feet.</summary>
    public void Knock(Vector3 impulse) => chest.AddForce(impulse, ForceMode.Impulse);

    /// <summary>A hit on the part nearest the point, which gains at most this speed: a light hand isn't flung off at bullet speed.</summary>
    public void Push(Vector3 point, Vector3 impulse, float mostSpeed)
    {
        Rigidbody nearest = null;
        float best = float.PositiveInfinity;
        foreach (var part in parts)
        {
            float distance = (part.worldCenterOfMass - point).sqrMagnitude;
            if (distance < best) { best = distance; nearest = part; }
        }
        Vector3 change = Vector3.ClampMagnitude(impulse / nearest.mass, mostSpeed);
        nearest.AddForceAtPosition(change * nearest.mass, point, ForceMode.Impulse);
    }

    /// <summary>Someone walking through leans on the parts ahead of them, never moving one faster than they walk.</summary>
    public void LeanOn(Vector3 feet, Vector3 velocity, float reach, float force)
    {
        Vector3 flat = Vector3.ProjectOnPlane(velocity, Vector3.up);
        float speed = flat.magnitude;
        if (speed < .05f) return;
        Vector3 direction = flat / speed;
        foreach (var part in parts)
        {
            Vector3 apart = part.worldCenterOfMass - feet;
            if (apart.y < -.2f || apart.y > 2f) continue;
            apart.y = 0f;
            if (apart.sqrMagnitude > reach * reach || Vector3.Dot(apart, direction) < 0f) continue;
            float missing = speed - Vector3.Dot(part.linearVelocity, direction);
            if (missing > 0f) part.AddForce(direction * Mathf.Min(missing, force * Time.fixedDeltaTime / part.mass), ForceMode.VelocityChange);
        }
    }

    /// <summary>This corpse's pose, for the PCs that follow it.</summary>
    public CorpsePose Capture()
    {
        var pose = new CorpsePose { Laid = true, Hips = parts[0].transform.position };
        foreach (var part in parts) pose.Add(part.transform.rotation);
        return pose;
    }

    /// <summary>Moves toward the pose the simulating PC reported. Parents come first in the list, so children land true.</summary>
    public void Follow(CorpsePose pose, float dt)
    {
        float blend = 1f - Mathf.Exp(-FollowRate * dt);
        var hips = parts[0].transform;
        hips.position = Vector3.Lerp(hips.position, pose.Hips, blend);
        for (int i = 0; i < parts.Count && i < pose.Parts; i++)
        {
            var bone = parts[i].transform;
            bone.rotation = Quaternion.Slerp(bone.rotation, pose.Rotation(i), blend);
        }
    }

    private void Build(Animator live)
    {
        Transform Bone(HumanBodyBones id)
        {
            int index = Array.IndexOf(liveBones, live.GetBoneTransform(id));
            return index >= 0 ? bones[index] : null;
        }
        ToNeutralPose(live.avatar, bones[0]);

        Transform hips = Bone(HumanBodyBones.Hips), spine = Bone(HumanBodyBones.Spine), head = Bone(HumanBodyBones.Head);
        Transform upperBody = Bone(HumanBodyBones.Chest) ?? spine, neck = Bone(HumanBodyBones.Neck) ?? head;
        Transform leftLeg = Bone(HumanBodyBones.LeftUpperLeg), rightLeg = Bone(HumanBodyBones.RightUpperLeg);
        Transform leftKnee = Bone(HumanBodyBones.LeftLowerLeg), rightKnee = Bone(HumanBodyBones.RightLowerLeg);
        Transform leftArm = Bone(HumanBodyBones.LeftUpperArm), rightArm = Bone(HumanBodyBones.RightUpperArm);
        Transform leftElbow = Bone(HumanBodyBones.LeftLowerArm), rightElbow = Bone(HumanBodyBones.RightLowerArm);

        Vector3 up = (head.position - hips.position).normalized;
        Vector3 right = Vector3.ProjectOnPlane(rightArm.position - leftArm.position, up).normalized;
        Vector3 forward = Vector3.Cross(right, up);
        float shoulders = Vector3.Distance(leftArm.position, rightArm.position);
        float legsApart = Vector3.Distance(leftLeg.position, rightLeg.position);

        var pelvis = Part(hips, HipsMass);
        Ball(pelvis, hips.position, legsApart * .75f);
        chest = Part(upperBody, ChestMass);
        Capsule(chest, spine.position, neck.position, shoulders * .35f);
        var skull = Part(head, HeadMass);
        Ball(skull, head.position + up * shoulders * .3f, shoulders * .3f);
        Link(chest, pelvis, right, forward, -25f, 25f, 15f);
        Link(skull, chest, right, forward, -40f, 30f, 30f);

        Leg(leftLeg, leftKnee, Bone(HumanBodyBones.LeftFoot));
        Leg(rightLeg, rightKnee, Bone(HumanBodyBones.RightFoot));
        // Seen from above, the two arms swing forward in opposite senses.
        Arm(leftArm, leftElbow, Bone(HumanBodyBones.LeftHand), up);
        Arm(rightArm, rightElbow, Bone(HumanBodyBones.RightHand), -up);
        Head = head;

        void Leg(Transform thigh, Transform knee, Transform foot)
        {
            var upper = Limb(thigh, knee, ThighMass, ThighThickness);
            var lower = Limb(knee, foot, ShinMass, ShinThickness);
            Link(upper, pelvis, right, forward, -20f, 70f, 30f);
            Link(lower, upper, right, forward, -120f, 0f, 0f);
        }

        void Arm(Transform shoulder, Transform elbow, Transform hand, Vector3 hinge)
        {
            var upper = Limb(shoulder, elbow, UpperArmMass, UpperArmThickness);
            var lower = Limb(elbow, hand, ForearmMass, ForearmThickness);
            Link(upper, chest, hinge, forward, -100f, 30f, 85f);
            Link(lower, upper, hinge, forward, -130f, 0f, 0f);
        }
    }

    // Zero on every muscle: legs straight, arms out, whatever the rig's bones are called or how they are rolled.
    private static void ToNeutralPose(Avatar avatar, Transform root)
    {
        var handler = new HumanPoseHandler(avatar, root);
        var pose = new HumanPose();
        handler.GetHumanPose(ref pose);
        Array.Clear(pose.muscles, 0, pose.muscles.Length);
        pose.bodyRotation = Quaternion.identity;
        handler.SetHumanPose(ref pose);
        handler.Dispose();
    }

    private Rigidbody Limb(Transform bone, Transform end, float mass, float thickness)
    {
        var part = Part(bone, mass);
        Capsule(part, bone.position, end.position, Vector3.Distance(bone.position, end.position) * thickness);
        return part;
    }

    private Rigidbody Part(Transform bone, float mass)
    {
        var part = bone.gameObject.AddComponent<Rigidbody>();
        part.isKinematic = true;
        part.mass = mass;
        part.angularDamping = Limp;
        part.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        part.solverIterations = SolverSteps;
        part.maxDepenetrationVelocity = Unsticking;
        part.excludeLayers = passes;
        parts.Add(part);
        return part;
    }

    private void Ball(Rigidbody part, Vector3 centre, float radius)
        => Shape<SphereCollider>(part, centre, Quaternion.identity).radius = radius / part.transform.lossyScale.x;

    private void Capsule(Rigidbody part, Vector3 start, Vector3 end, float radius)
    {
        var capsule = Shape<CapsuleCollider>(part, (start + end) * .5f, Quaternion.FromToRotation(Vector3.up, end - start));
        float scale = part.transform.lossyScale.x;
        capsule.radius = radius / scale;
        capsule.height = (Vector3.Distance(start, end) + radius * 2f) / scale;
    }

    // A child of its own, because rigs roll their bones every way and a collider only lines up with its object's axes.
    private T Shape<T>(Rigidbody part, Vector3 position, Quaternion rotation) where T : Collider
    {
        var shape = new GameObject("Shape", typeof(T));
        shape.layer = layer;
        shape.transform.SetParent(part.transform, false);
        shape.transform.SetPositionAndRotation(position, rotation);
        var collider = shape.GetComponent<T>();
        shapes.Add(collider);
        return collider;
    }

    private static void Link(Rigidbody part, Rigidbody parent, Vector3 twist, Vector3 swing, float low, float high, float swingLimit)
    {
        var joint = part.gameObject.AddComponent<CharacterJoint>();
        joint.connectedBody = parent;
        joint.axis = part.transform.InverseTransformDirection(twist);
        joint.swingAxis = part.transform.InverseTransformDirection(swing);
        joint.enablePreprocessing = false;
        joint.enableProjection = true;
        joint.lowTwistLimit = new SoftJointLimit { limit = low };
        joint.highTwistLimit = new SoftJointLimit { limit = high };
        joint.swing1Limit = new SoftJointLimit { limit = swingLimit };
        joint.swing2Limit = new SoftJointLimit { limit = 0f };
    }
}
