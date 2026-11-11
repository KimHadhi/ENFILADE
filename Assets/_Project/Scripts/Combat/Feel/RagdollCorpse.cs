using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A doll for a humanoid body: one rigid part for each main bone, with no skin or skeleton of its own. Building one in
/// the middle of a fight would hitch, so it is made once up front and stowed, switched off. Dying lays it over the live
/// body, which then takes the doll's turns every frame (PoseBody). Physics never writes a real bone, so nothing here can
/// leave a living body broken, and whatever the body wears goes on working. Simulated, the doll runs physics and can be
/// pushed; otherwise it follows the pose another PC's corpse reports (Capture, Follow), so every PC shows one body.
/// It falls as a body does, not as a puppet whose strings were cut: each limb carries on as it was moving (Launch), the
/// muscles hold the pose it died in and give out over a moment (Brace), limbs land on the body instead of in it, and the
/// hands and feet have shapes of their own, so they stay out of the floor. Where the rig has a chest bone the trunk bends
/// at the waist. Every joint turns the ways a body's does: a limb turns about its own length, so a bent arm or knee falls
/// over instead of standing up stiff, the head turns and the trunk twists, and the bones between the parts (collarbones,
/// neck, hands, fingers, feet) go slack with the muscles. Limits are measured from the pose a joint is made in, so the
/// hinges and the trunk are made in the neutral pose, a body's and not whatever pose it died in, and shoulders and hips
/// with the limb turned to the middle of its reach (Socket). Twist limits read as Unity's ragdoll wizard does: negative
/// bends a knee, an elbow or a nod forward.
/// </summary>
public sealed class RagdollCorpse : MonoBehaviour, IDamageReceiver
{
    // Kilos. Joints care how the parts weigh against each other: keep the heaviest within ten times the lightest. A trunk
    // with a waist shares its weight three ways.
    private const float HipsMass = 12f, ChestMass = 12f, HeadMass = 5f, ThighMass = 8f, ShinMass = 5f, UpperArmMass = 3f, ForearmMass = 2f;
    private const float WaistedHipsMass = 10f, WaistMass = 6f, WaistedChestMass = 10f;
    private const float ThighThickness = .22f, ShinThickness = .18f, UpperArmThickness = .2f, ForearmThickness = .16f;
    // A share of the distance between the hip joints.
    private const float WaistThickness = .62f;
    // Shares of the shin: how high the ankle stands over the sole, and how far ahead the ball of the foot is without a toe bone.
    private const float AnkleHeight = .18f, BallReach = .35f;
    // The middle of a limb's reach, from the neutral pose (degrees): an arm let down and brought forward, a thigh raised.
    private const float ArmDrop = 45f, ArmForward = 30f, ThighRaise = 45f;
    // How far fingers go slack toward straight: a dead hand stays a little curled. The bones between the parts take at
    // least this long to go slack (s), so even a body that drops limp at once doesn't snap its hands open.
    private const float FingerSlack = .6f, LeastSlackTime = .15f;
    private const float Limp = .5f, Unsticking = 2f, FollowRate = 30f;
    // A bone last seen longer ago than this gives no motion to carry on. Shapes that started a fall inside each other are
    // checked this often to see whether they have come apart.
    private const float FreshMotion = .1f, ApartCheck = .2f;
    private const int SolverSteps = 12;

    // A hand or a foot rides on its forearm or shin, lined up when the doll is laid with where the real one goes slack.
    private struct Extremity
    {
        public Collider Shape;
        public Transform Bone;
        public HumanBodyBones Id;
        public Vector3 Place;
        public Quaternion Turn, Rest;
    }

    // A human bone between the parts, easing from how it died to how it rests, by this share of the way.
    private struct Slack
    {
        public Transform Bone;
        public HumanBodyBones Id;
        public Quaternion Rest, From;
        public float Share;
    }

    // Two shapes on parts that aren't joined. Built: they overlap even in the neutral pose, where a body is all of a piece.
    private struct Touch
    {
        public Collider A, B;
        public bool Built;
    }

    private readonly List<Rigidbody> parts = new List<Rigidbody>();
    // The real bone each part turns, by the part's place in the list, and which human bone it is (for Bind).
    private readonly List<Transform> bones = new List<Transform>();
    private readonly List<HumanBodyBones> boneIds = new List<HumanBodyBones>();
    // While building: which human bone each real bone is.
    private Dictionary<Transform, HumanBodyBones> humanIds;
    private readonly List<Collider> shapes = new List<Collider>();
    // The part each shape is on, by the shape's place in its list.
    private readonly List<Rigidbody> shapeParts = new List<Rigidbody>();
    private readonly List<Extremity> extremities = new List<Extremity>();
    private readonly List<Slack> slack = new List<Slack>();
    private readonly List<Touch> touches = new List<Touch>();
    // Touches let through because they started this fall inside each other, until they come apart.
    private readonly List<Touch> tangled = new List<Touch>();
    private readonly HashSet<(Rigidbody, Rigidbody)> joined = new HashSet<(Rigidbody, Rigidbody)>();
    private readonly List<ConfigurableJoint> muscles = new List<ConfigurableJoint>();
    // Each muscle's part as turned against its parent in the neutral pose, which is the joint's own zero.
    private readonly List<Quaternion> neutral = new List<Quaternion>();
    private Vector3[] lastPlaces;
    private Quaternion[] lastTurns;
    private Transform[] measured, real;
    private Rigidbody chest;
    private int layer;
    private LayerMask passes;
    private float lastSeen = float.NegativeInfinity, pull, friction, toneSeconds, toneLeft, nextApartCheck, laidAt, slackTime;
    private bool laid, simulated, slackening;

    /// <summary>The real head, which is where the eyes are.</summary>
    public Transform Head { get; private set; }
    /// <summary>The rig it was made for. Any body with this avatar can borrow it (Bind).</summary>
    public Avatar Avatar { get; private set; }
    /// <summary>Its colliders: a knocked-down zombie lets bullets past them to its own hitboxes (ZombieBody).</summary>
    public IReadOnlyList<Collider> Shapes => shapes;
    /// <summary>Bullets, blows and shoves that reach it. It takes no damage; whoever laid it decides how far they push it.</summary>
    public event Action<DamageHit> Struck;

    /// <summary>
    /// Makes a doll of this live model and stows it. It touches what's in solid and the parts of corpses, its own among
    /// them; its shapes sit on this layer. Safe to call from anywhere, a physics callback included: a body with no doll
    /// stowed for its rig gets one made on the spot, and a slide into a zombie knocks it down from inside
    /// OnControllerColliderHit, where Unity refuses DestroyImmediate. It used to copy the whole model and strip the copy
    /// with DestroyImmediate; refused there, the copy stayed standing inside the corpse (user, 2026-11-10: the errors).
    /// </summary>
    public static RagdollCorpse Prepare(Animator live, int layer, LayerMask solid)
    {
        if (live == null || !live.isHuman) return null;
        var root = new GameObject("Corpse (" + live.name + ")");
        var corpse = root.AddComponent<RagdollCorpse>();
        corpse.Avatar = live.avatar;
        corpse.layer = layer;
        corpse.passes = ~(solid | (1 << layer));
        // The neutral pose is measured on a copy of the skeleton, away from the real bones; it goes at the frame's end.
        var skeleton = Skeleton(live.transform);
        bool built = corpse.Build(live, skeleton);
        Destroy(skeleton.gameObject);
        // A rig whose humanoid can't find its bones (an outfit's bone of the same name under one of the body's, before
        // OutfitDresser renamed them) makes no doll, and what was made for it goes: it once threw here every frame,
        // leaving a corpse and a skeleton behind each time, and the game crawled (the user, 2026-11-12).
        if (!built)
        {
            Destroy(root);
            return null;
        }
        corpse.Stow();
        return corpse;
    }

    // The model's transforms and nothing else, each named and placed as its own and in the same order (Build pairs the
    // two by their place in GetComponentsInChildren): a skeleton to pose, with no scripts, Animator, skin or hitboxes to
    // wake or to strip, and far cheaper to make than a copy of the model.
    private static Transform Skeleton(Transform model)
    {
        var real = model.GetComponentsInChildren<Transform>(true);
        var copies = new Transform[real.Length];
        var places = new Dictionary<Transform, int>(real.Length);
        for (int i = 0; i < real.Length; i++)
        {
            places[real[i]] = i;
            var copy = new GameObject(real[i].name).transform;
            if (i == 0)
            {
                // Under nothing, at the size the model has under whatever it hangs from.
                copy.SetPositionAndRotation(model.position, model.rotation);
                copy.localScale = model.lossyScale;
            }
            else
            {
                // Depth first, so its parent is already made and brothers are added in their order.
                copy.SetParent(copies[places[real[i].parent]], false);
                copy.localPosition = real[i].localPosition;
                copy.localRotation = real[i].localRotation;
                copy.localScale = real[i].localScale;
            }
            copies[i] = copy;
        }
        return copies[0];
    }

    /// <summary>
    /// From now on turns this body instead, which must be on the same rig (its Avatar): zombies of one pack share their
    /// skeleton, so a few stowed dolls serve all of them in turn. Its human bones come indexed by HumanBodyBones (see
    /// HumanBones), read while its Animator was on, since a body may die with it off (knocked down). Call it while
    /// stowed, before Lay.
    /// </summary>
    public void Bind(Transform[] human)
    {
        for (int i = 0; i < bones.Count; i++) bones[i] = human[(int)boneIds[i]];
        for (int i = 0; i < extremities.Count; i++)
        {
            var end = extremities[i];
            end.Bone = human[(int)end.Id];
            extremities[i] = end;
        }
        for (int i = 0; i < slack.Count; i++)
        {
            var bone = slack[i];
            bone.Bone = human[(int)bone.Id];
            slack[i] = bone;
        }
        Head = human[(int)HumanBodyBones.Head];
        lastSeen = float.NegativeInfinity;
    }

    /// <summary>Each human bone of this body, indexed by HumanBodyBones (null where the rig has none), for Bind.</summary>
    public static Transform[] HumanBones(Animator live)
    {
        var human = new Transform[(int)HumanBodyBones.LastBone];
        if (live == null || !live.isHuman) return human;
        for (var id = HumanBodyBones.Hips; id < HumanBodyBones.LastBone; id++) human[(int)id] = live.GetBoneTransform(id);
        return human;
    }

    /// <summary>Every part has about stopped: slower than this (m/s), and turning slower than four times that (rad/s).</summary>
    public bool Settled(float speed)
    {
        foreach (var part in parts)
            if (!part.isKinematic && !part.IsSleeping()
                && (part.linearVelocity.sqrMagnitude > speed * speed || part.angularVelocity.sqrMagnitude > 16f * speed * speed))
                return false;
        return true;
    }

    /// <summary>Notes where the live body's bones are this frame, so a fall can carry on their motion.</summary>
    public void Track()
    {
        for (int i = 0; i < bones.Count; i++)
        {
            lastPlaces[i] = bones[i].position;
            lastTurns[i] = bones[i].rotation;
        }
        lastSeen = Time.time;
    }

    /// <summary>
    /// Takes the live body's pose at this moment. Simulated, it drops; otherwise it waits to follow. The bones between
    /// the parts go slack over this long (s), the same on every PC.
    /// </summary>
    public void Lay(bool simulated, float slackSeconds)
    {
        laid = true;
        this.simulated = simulated;
        for (int i = 0; i < slack.Count; i++)
        {
            var bone = slack[i];
            bone.From = bone.Bone.localRotation;
            slack[i] = bone;
        }
        laidAt = Time.time;
        slackTime = Mathf.Max(LeastSlackTime, slackSeconds);
        slackening = true;
        for (int i = 0; i < parts.Count; i++) parts[i].transform.SetPositionAndRotation(bones[i].position, bones[i].rotation);
        foreach (var end in extremities)
        {
            Quaternion turn = end.Bone.parent.rotation * end.Rest;
            end.Shape.transform.SetPositionAndRotation(end.Bone.position + turn * Vector3.Scale(end.Place, end.Bone.lossyScale), turn * end.Turn);
        }
        foreach (var shape in shapes) shape.enabled = true;
        Physics.SyncTransforms();
        if (simulated) Untangle();
        foreach (var part in parts)
        {
            part.interpolation = simulated ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            part.isKinematic = !simulated;
        }
    }

    /// <summary>
    /// Starts every part moving as its bone was: the body's speed plus the limb's own swing, held to these, because an
    /// animation can jump a bone in a way no limb could move.
    /// </summary>
    public void Launch(Vector3 carried, float mostLimbSpeed, float mostLimbSpin)
    {
        float dt = Time.time - lastSeen;
        bool fresh = dt > 0f && dt < FreshMotion;
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            Vector3 swing = Vector3.zero, spin = Vector3.zero;
            if (fresh)
            {
                swing = Vector3.ClampMagnitude((bones[i].position - lastPlaces[i]) / dt - carried, mostLimbSpeed);
                (bones[i].rotation * Quaternion.Inverse(lastTurns[i])).ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f) angle -= 360f;
                if (Mathf.Abs(angle) > .01f) spin = Vector3.ClampMagnitude(axis * (angle * Mathf.Deg2Rad / dt), mostLimbSpin);
            }
            part.angularVelocity = spin;
            // The bone is what the part turns about, so its centre of mass swings round it.
            part.linearVelocity = carried + swing + Vector3.Cross(spin, part.worldCenterOfMass - bones[i].position);
        }
    }

    /// <summary>
    /// The muscles hold the pose it died in, pulling back at this rate (Hz), and give out over this long, so a body sags
    /// into its knees and folds as it goes instead of dropping all at once. Friction stays in the joints after (1/s), so it
    /// settles instead of twitching. Call it after Lay.
    /// </summary>
    public void Brace(float hold, float seconds, float friction)
    {
        pull = 2f * Mathf.PI * hold;
        this.friction = friction;
        toneSeconds = toneLeft = seconds;
        for (int i = 0; i < muscles.Count; i++)
        {
            var muscle = muscles[i];
            Quaternion now = Quaternion.Inverse(muscle.connectedBody.transform.rotation) * muscle.transform.rotation;
            // A drive's target is the turn away from the joint's zero, written the other way round.
            muscle.targetRotation = Quaternion.Inverse(now) * neutral[i];
        }
        SetTone(seconds > 0f ? 1f : 0f);
    }

    /// <summary>
    /// Turns the real body to this doll. Parents come first in the list, so children land true, and the bones keep their
    /// lengths however far the joints stretch. Following another PC there is no physics to place the parts, so the bones
    /// do, and blows land where the body is seen.
    /// </summary>
    public void PoseBody()
    {
        // The bones between the parts first: the parts' bones hang from some of them.
        if (slackening) GoSlack();
        bones[0].position = parts[0].transform.position;
        for (int i = 0; i < parts.Count; i++)
        {
            bones[i].rotation = parts[i].transform.rotation;
            if (!simulated && i > 0) parts[i].transform.position = bones[i].position;
        }
    }

    /// <summary>Out of the world until the next death. The joints stay as made.</summary>
    public void Stow()
    {
        laid = slackening = false;
        toneLeft = friction = 0f;
        SetTone(0f);
        tangled.Clear();
        foreach (var part in parts)
        {
            part.interpolation = RigidbodyInterpolation.None;
            if (part.isKinematic) continue;
            part.linearVelocity = Vector3.zero;
            part.angularVelocity = Vector3.zero;
            part.isKinematic = true;
        }
        foreach (var shape in shapes) shape.enabled = false;
    }

    /// <summary>
    /// Holds every part where it lies (kinematic): still shot and struck, still posing the body, costing no physics.
    /// Thaw lets it go again.
    /// </summary>
    public void Hold()
    {
        foreach (var part in parts)
        {
            if (part.isKinematic) continue;
            part.linearVelocity = Vector3.zero;
            part.angularVelocity = Vector3.zero;
            part.isKinematic = true;
        }
    }

    /// <summary>After Hold: physics moves it again, where this PC runs it.</summary>
    public void Thaw()
    {
        if (!laid || !simulated) return;
        foreach (var part in parts)
        {
            part.isKinematic = false;
            part.WakeUp();
        }
    }

    /// <summary>From now on this PC runs it, from the pose it shows (the PC that ran it is gone).</summary>
    public void Simulate()
    {
        if (!laid || simulated) return;
        simulated = true;
        foreach (var part in parts)
        {
            part.interpolation = RigidbodyInterpolation.Interpolate;
            part.isKinematic = false;
        }
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

    /// <summary>Moves toward the pose the simulating PC reported.</summary>
    public void Follow(CorpsePose pose, float dt)
    {
        float blend = 1f - Mathf.Exp(-FollowRate * dt);
        var hips = parts[0].transform;
        hips.position = Vector3.Lerp(hips.position, pose.Hips, blend);
        for (int i = 0; i < parts.Count && i < pose.Parts; i++)
        {
            var part = parts[i].transform;
            part.rotation = Quaternion.Slerp(part.rotation, pose.Rotation(i), blend);
        }
    }

    private void FixedUpdate()
    {
        if (!laid || !simulated) return;
        if (toneLeft > 0f)
        {
            toneLeft = Mathf.Max(0f, toneLeft - Time.fixedDeltaTime);
            SetTone(toneLeft / toneSeconds);
        }
        if (tangled.Count > 0 && Time.fixedTime >= nextApartCheck) ComeApart();
    }

    // The small muscles let go with the big ones: collarbones drop, hands open, feet hang. Once slack, nothing writes them.
    private void GoSlack()
    {
        float done = Mathf.Clamp01((Time.time - laidAt) / slackTime), eased = 1f - (1f - done) * (1f - done);
        foreach (var bone in slack)
            bone.Bone.localRotation = Quaternion.Slerp(bone.From, Quaternion.Slerp(bone.From, bone.Rest, bone.Share), eased);
        slackening = done < 1f;
    }

    // Eased out, so the muscles go slowly at first and the knees give way before the rest.
    private void SetTone(float share)
    {
        float strength = share * share;
        var drive = new JointDrive
        {
            positionSpring = pull * pull * strength,
            positionDamper = 2f * pull * strength + friction,
            maximumForce = float.MaxValue,
            useAcceleration = true
        };
        foreach (var muscle in muscles) muscle.slerpDrive = drive;
    }

    // Limbs land on the body instead of in it. Parts that start inside each other would burst apart, so they pass through
    // until they have come apart.
    private void Untangle()
    {
        tangled.Clear();
        foreach (var touch in touches)
        {
            bool spared = !touch.Built && Overlap(touch.A, touch.B);
            Physics.IgnoreCollision(touch.A, touch.B, touch.Built || spared);
            if (spared) tangled.Add(touch);
        }
        nextApartCheck = Time.fixedTime + ApartCheck;
    }

    private void ComeApart()
    {
        nextApartCheck = Time.fixedTime + ApartCheck;
        for (int i = tangled.Count - 1; i >= 0; i--)
        {
            if (Overlap(tangled[i].A, tangled[i].B)) continue;
            Physics.IgnoreCollision(tangled[i].A, tangled[i].B, false);
            tangled.RemoveAt(i);
        }
    }

    private static bool Overlap(Collider a, Collider b) => Physics.ComputePenetration(
        a, a.transform.position, a.transform.rotation, b, b.transform.position, b.transform.rotation, out _, out _);

    // False, building nothing, when the humanoid lacks a bone the doll hangs on.
    private bool Build(Animator live, Transform model)
    {
        measured = model.GetComponentsInChildren<Transform>(true);
        real = live.GetComponentsInChildren<Transform>(true);
        humanIds = new Dictionary<Transform, HumanBodyBones>();
        for (var id = HumanBodyBones.Hips; id < HumanBodyBones.LastBone; id++)
        {
            var bone = live.GetBoneTransform(id);
            if (bone != null) humanIds[bone] = id;
        }
        Transform Bone(HumanBodyBones id)
        {
            int index = Array.IndexOf(real, live.GetBoneTransform(id));
            return index >= 0 ? measured[index] : null;
        }
        ToNeutralPose(live.avatar, model);

        Transform hips = Bone(HumanBodyBones.Hips), spine = Bone(HumanBodyBones.Spine), waistTop = Bone(HumanBodyBones.Chest);
        Transform head = Bone(HumanBodyBones.Head), upperBody = waistTop ?? spine, neck = Bone(HumanBodyBones.Neck) ?? head;
        Transform leftLeg = Bone(HumanBodyBones.LeftUpperLeg), rightLeg = Bone(HumanBodyBones.RightUpperLeg);
        Transform leftKnee = Bone(HumanBodyBones.LeftLowerLeg), rightKnee = Bone(HumanBodyBones.RightLowerLeg);
        Transform leftArm = Bone(HumanBodyBones.LeftUpperArm), rightArm = Bone(HumanBodyBones.RightUpperArm);
        Transform leftElbow = Bone(HumanBodyBones.LeftLowerArm), rightElbow = Bone(HumanBodyBones.RightLowerArm);
        if (hips == null || spine == null || head == null || leftLeg == null || rightLeg == null || leftKnee == null || rightKnee == null
            || leftArm == null || rightArm == null || leftElbow == null || rightElbow == null)
        {
            measured = real = null;
            humanIds = null;
            return false;
        }

        Vector3 up = (head.position - hips.position).normalized;
        Vector3 right = Vector3.ProjectOnPlane(rightArm.position - leftArm.position, up).normalized;
        Vector3 forward = Vector3.Cross(right, up);
        float shoulders = Vector3.Distance(leftArm.position, rightArm.position);
        float legsApart = Vector3.Distance(leftLeg.position, rightLeg.position);

        bool waisted = waistTop != null;
        var pelvis = Part(hips, waisted ? WaistedHipsMass : HipsMass);
        Ball(pelvis, hips.position, legsApart * .75f);
        var below = pelvis;
        if (waisted)
        {
            var waist = Part(spine, WaistMass);
            Capsule(waist, spine.position, waistTop.position, legsApart * WaistThickness);
            Link(waist, pelvis, right, forward, -20f, 20f, 10f, 15f);
            below = waist;
        }
        // The trunk bends forward and back, leans to the side and twists, each joint a share of it.
        chest = Part(upperBody, waisted ? WaistedChestMass : ChestMass);
        Capsule(chest, upperBody.position, neck.position, shoulders * .35f);
        if (waisted) Link(chest, below, right, forward, -20f, 20f, 10f, 15f);
        else Link(chest, pelvis, right, forward, -25f, 25f, 15f, 25f);
        // A nod, a tilt and a turn: a dead head rolls to the side.
        var skull = Part(head, HeadMass);
        Ball(skull, head.position + up * shoulders * .3f, shoulders * .3f);
        Link(skull, chest, right, forward, -40f, 30f, 30f, 55f);

        Leg(leftLeg, leftKnee, Bone(HumanBodyBones.LeftFoot), Bone(HumanBodyBones.LeftToes));
        Leg(rightLeg, rightKnee, Bone(HumanBodyBones.RightFoot), Bone(HumanBodyBones.RightToes));
        // Seen from above, the two arms swing forward in opposite senses.
        Arm(leftArm, leftElbow, Bone(HumanBodyBones.LeftHand), Bone(HumanBodyBones.LeftMiddleProximal), up);
        Arm(rightArm, rightElbow, Bone(HumanBodyBones.RightHand), Bone(HumanBodyBones.RightMiddleProximal), -up);
        Head = live.GetBoneTransform(HumanBodyBones.Head);
        FindTouches();
        FindSlack(live);
        lastPlaces = new Vector3[bones.Count];
        lastTurns = new Quaternion[bones.Count];
        measured = real = null;
        humanIds = null;
        return true;

        // A knee bends one way and turns a little; the hip is a ball centred on a thigh raised halfway, so it bends right
        // up in front but only a little behind, and the thigh turns about its length, so a bent knee falls to the side.
        void Leg(Transform thigh, Transform knee, Transform foot, Transform toes)
        {
            var upper = Limb(thigh, knee, ThighMass, ThighThickness);
            var lower = Limb(knee, foot, ShinMass, ShinThickness);
            Link(lower, upper, right, forward, -140f, 0f, 0f, 10f);
            Foot(lower, knee, foot, toes);
            Vector3 down = (knee.position - thigh.position).normalized, ahead = Vector3.ProjectOnPlane(forward, down).normalized;
            float raise = ThighRaise * Mathf.Deg2Rad;
            Socket(upper, pelvis, thigh.position, down, down * Mathf.Cos(raise) + ahead * Mathf.Sin(raise), right, 35f, 75f, 40f, upper, lower);
        }

        // An elbow bends one way and the forearm turns about its length; the shoulder is a ball centred on an arm let down
        // and brought forward, which reaches up, down, ahead and a little behind, and turns about its length.
        void Arm(Transform shoulder, Transform elbow, Transform hand, Transform knuckle, Vector3 hinge)
        {
            var upper = Limb(shoulder, elbow, UpperArmMass, UpperArmThickness);
            var lower = Limb(elbow, hand, ForearmMass, ForearmThickness);
            Link(lower, upper, hinge, forward, -145f, 0f, 0f, 80f);
            Hand(lower, elbow, hand, knuckle);
            Vector3 along = (elbow.position - shoulder.position).normalized, side = Vector3.ProjectOnPlane(along, up).normalized;
            float drop = ArmDrop * Mathf.Deg2Rad, ahead = ArmForward * Mathf.Deg2Rad;
            Vector3 centre = (side * Mathf.Cos(ahead) + forward * Mathf.Sin(ahead)) * Mathf.Cos(drop) - up * Mathf.Sin(drop);
            Socket(upper, chest, shoulder.position, along, centre, forward, 70f, 90f, 80f, upper, lower);
        }

        // Heel to toe along the sole, which lies the ankle's height below it.
        void Foot(Rigidbody shin, Transform knee, Transform ankle, Transform toes)
        {
            float length = Vector3.Distance(knee.position, ankle.position), height = length * AnkleHeight, radius = height * .45f;
            Vector3 ball = toes != null ? toes.position : ankle.position + forward * (length * BallReach);
            Vector3 along = Vector3.ProjectOnPlane(ball - ankle.position, up);
            if (along.sqrMagnitude < 1e-6f) along = forward * (length * BallReach);
            Vector3 sole = ankle.position - up * (height - radius);
            Attach(Capsule(shin, sole - along * .15f, sole + along * 1.1f, radius), ankle);
        }

        // Wrist to fingertips, about twice the way from the wrist to the knuckles.
        void Hand(Rigidbody forearm, Transform elbow, Transform hand, Transform knuckle)
        {
            Vector3 palm = knuckle != null ? knuckle.position - hand.position : (hand.position - elbow.position) * .38f;
            Attach(Capsule(forearm, hand.position + palm * .3f, hand.position + palm * 1.7f, palm.magnitude * .35f), hand);
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

    // Every human bone that no part turns, but the eyes and the jaw, which keep the face as it died. Each rests as in the
    // neutral pose, fingers only part of the way.
    private void FindSlack(Animator live)
    {
        for (var id = HumanBodyBones.Hips; id < HumanBodyBones.LastBone; id++)
        {
            if (id == HumanBodyBones.LeftEye || id == HumanBodyBones.RightEye || id == HumanBodyBones.Jaw) continue;
            var bone = live.GetBoneTransform(id);
            if (bone == null || bones.Contains(bone)) continue;
            bool finger = id >= HumanBodyBones.LeftThumbProximal && id <= HumanBodyBones.RightLittleDistal;
            slack.Add(new Slack { Bone = bone, Id = id, Rest = measured[Array.IndexOf(real, bone)].localRotation, Share = finger ? FingerSlack : 1f });
        }
    }

    // A ball joint, made with the limb turned from where it points to the middle of its reach and then back: a joint
    // measures its limits from the pose it is made in, and a cone round the middle covers a body's reach where one round
    // the neutral pose can't. Twist turns the limb about its own length. Every limit is the same either way, so a ball joint
    // can't come out mirrored.
    private void Socket(Rigidbody part, Rigidbody parent, Vector3 pivot, Vector3 from, Vector3 centre, Vector3 swing,
        float twist, float swing1, float swing2, params Rigidbody[] limb)
    {
        Quaternion turn = Quaternion.FromToRotation(from, centre);
        Turn(limb, pivot, turn);
        Link(part, parent, centre, Vector3.ProjectOnPlane(turn * swing, centre).normalized, -twist, twist, swing1, swing2);
        Turn(limb, pivot, Quaternion.Inverse(turn));
    }

    private static void Turn(Rigidbody[] limb, Vector3 pivot, Quaternion turn)
    {
        foreach (var part in limb)
            part.transform.SetPositionAndRotation(pivot + turn * (part.transform.position - pivot), turn * part.transform.rotation);
        Physics.SyncTransforms();
    }

    // Shapes on parts that aren't joined can touch. Those that overlap even in the neutral pose never do.
    private void FindTouches()
    {
        for (int i = 0; i < shapes.Count; i++)
            for (int j = i + 1; j < shapes.Count; j++)
            {
                Rigidbody a = shapeParts[i], b = shapeParts[j];
                if (a == b || joined.Contains((a, b)) || joined.Contains((b, a))) continue;
                touches.Add(new Touch { A = shapes[i], B = shapes[j], Built = Overlap(shapes[i], shapes[j]) });
            }
    }

    private Rigidbody Limb(Transform bone, Transform end, float mass, float thickness)
    {
        var part = Part(bone, mass);
        Capsule(part, bone.position, end.position, Vector3.Distance(bone.position, end.position) * thickness);
        return part;
    }

    // Lined up with its bone, so the two share a turn however the rig rolls its bones.
    private Rigidbody Part(Transform bone, float mass)
    {
        var part = new GameObject(bone.name, typeof(Rigidbody)).GetComponent<Rigidbody>();
        part.gameObject.layer = layer;
        part.transform.SetParent(transform, false);
        part.transform.SetPositionAndRotation(bone.position, bone.rotation);
        part.isKinematic = true;
        part.mass = mass;
        part.angularDamping = Limp;
        part.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        part.solverIterations = SolverSteps;
        part.maxDepenetrationVelocity = Unsticking;
        part.excludeLayers = passes;
        parts.Add(part);
        var realBone = real[Array.IndexOf(measured, bone)];
        bones.Add(realBone);
        boneIds.Add(humanIds[realBone]);
        return part;
    }

    // Kept as it sits against its bone in the neutral pose, to be lined up with the real bone when laid.
    private void Attach(Collider shape, Transform bone)
    {
        var realBone = real[Array.IndexOf(measured, bone)];
        extremities.Add(new Extremity
        {
            Shape = shape,
            Bone = realBone,
            Id = humanIds[realBone],
            Place = bone.InverseTransformPoint(shape.transform.position),
            Turn = Quaternion.Inverse(bone.rotation) * shape.transform.rotation,
            Rest = bone.localRotation
        });
    }

    private void Ball(Rigidbody part, Vector3 centre, float radius)
        => Shape<SphereCollider>(part, centre, Quaternion.identity).radius = radius;

    private CapsuleCollider Capsule(Rigidbody part, Vector3 start, Vector3 end, float radius)
    {
        var capsule = Shape<CapsuleCollider>(part, (start + end) * .5f, Quaternion.FromToRotation(Vector3.up, end - start));
        capsule.radius = radius;
        capsule.height = Vector3.Distance(start, end) + radius * 2f;
        return capsule;
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
        shapeParts.Add(part);
        return collider;
    }

    // Twist turns about the first axis; swing1 about the second, swing2 about the third, which together bound a cone.
    private void Link(Rigidbody part, Rigidbody parent, Vector3 twist, Vector3 swing, float low, float high, float swing1, float swing2)
    {
        var joint = part.gameObject.AddComponent<CharacterJoint>();
        joint.connectedBody = parent;
        joint.axis = part.transform.InverseTransformDirection(twist);
        joint.swingAxis = part.transform.InverseTransformDirection(swing);
        joint.enablePreprocessing = false;
        joint.enableProjection = true;
        joint.lowTwistLimit = new SoftJointLimit { limit = low };
        joint.highTwistLimit = new SoftJointLimit { limit = high };
        joint.swing1Limit = new SoftJointLimit { limit = swing1 };
        joint.swing2Limit = new SoftJointLimit { limit = swing2 };
        // The muscles are a second joint with no limits that only pulls toward a pose, so the limits above stay as they
        // are. A slerp drive needs every turn free.
        var muscle = part.gameObject.AddComponent<ConfigurableJoint>();
        muscle.connectedBody = parent;
        muscle.enablePreprocessing = false;
        muscle.xMotion = muscle.yMotion = muscle.zMotion = ConfigurableJointMotion.Free;
        muscle.angularXMotion = muscle.angularYMotion = muscle.angularZMotion = ConfigurableJointMotion.Free;
        muscle.rotationDriveMode = RotationDriveMode.Slerp;
        muscles.Add(muscle);
        neutral.Add(Quaternion.Inverse(parent.transform.rotation) * part.transform.rotation);
        joined.Add((part, parent));
    }
}
