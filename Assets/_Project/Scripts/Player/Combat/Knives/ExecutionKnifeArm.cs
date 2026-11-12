using UnityEngine;

/// <summary>
/// The body's arms through an execution's knife stroke (ExecutionKnife drives the stroke, KnifeStroke holds its path):
/// the right hand draws the class's knife from the hip, raises it, drives it down into the victim's neck and puts it away,
/// while the left takes hold of the victim's head; the knife rides in the right fist as ClassKnives seats it. Both wrists
/// follow the path from their own shoulders in their own arm's lengths, as RepairArm's loops do, and are drawn toward the
/// victim (the neck, the head) by each key's aim, so the blade meets the neck whatever body holds it. The same on every PC:
/// each copy's ExecutionKnife plays the stroke its owner's PC sent. The gun is put away meanwhile (WeaponController reads
/// ExecutionKnife.HoldsHands), so both arms are free. After the hands, the gun and FPS Animation Framework (GunFramework
/// + 5), before the off-hand uses, which take the left arm when they run (none does while executing), and before the
/// cloth, so a sleeve follows. Rig-agnostic: any humanoid a modder brings.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.GunFramework + 5)]
public sealed class ExecutionKnifeArm : MonoBehaviour
{
    // The fingers in order (index, middle, ring, little, thumb), three joints each from the knuckle out, and a finger's
    // curl shared out over its joints, knuckle first (RepairArm's).
    private const int Index = 0, Middle = 1, Ring = 2, Little = 3, Thumb = 4;
    private static readonly float[] CurlShare = { .4f, .35f, .25f };
    // Where the wrists aim (m): the right fist over the back of the neck by this plus a share of the knife's reach (so a
    // long blade goes in deep and a short one still meets it), the left hand on top of the head and a little toward the
    // player, as a hand grips a head to hold it.
    private const float NeckBack = .07f, FistOverNeck = .05f, ReachShareOverNeck = .3f, MostOverNeck = .23f, HeadAbove = .1f, HeadBack = .07f;

    private sealed class Side
    {
        public bool Right;
        public Transform Upper, Lower, Hand;
        public readonly Transform[] Fingers = new Transform[15];
        public readonly Transform[] Turned = new Transform[18];
        public readonly Quaternion[] Framed = new Quaternion[18];
        public int TurnedCount;
        public Vector3 JointAxis, HandAxis;
        public float ArmLength, HandLength;
    }

    private PlayerMovement movement;
    private ExecutionKnife driver;
    private Animator animator;
    private Transform eyes, holder;
    private readonly Side right = new Side { Right = true }, left = new Side();
    private bool bound;
    private GameObject model;
    private string worn;
    private Vector3 modelBlade = Vector3.back, modelEdge = Vector3.down;
    private float reach = .2f;

    /// <summary>A player's knife arms, made on their body's model the first time; null without a humanoid body.</summary>
    internal static ExecutionKnifeArm Of(PlayerMovement who)
    {
        if (who == null || !who.TryGetComponent(out PlayerBody body)) return null;
        var bodyModel = body.BodyAnimator;
        if (bodyModel == null || !bodyModel.isHuman) return null;
        return bodyModel.TryGetComponent(out ExecutionKnifeArm arm) ? arm : bodyModel.gameObject.AddComponent<ExecutionKnifeArm>();
    }

    /// <summary>
    /// This knife's model in the right fist's holder, made once per model and kept hidden until a stroke holds it: the
    /// class's knife is put on before its first execution, so drawing it never waits on loading it.
    /// </summary>
    internal void Wear(ClassKnives.Knife knife)
    {
        if (knife == null || !Bind()) return;
        if (holder == null)
        {
            holder = new GameObject("Execution Knife").transform;
            holder.SetParent(transform, false);
            holder.gameObject.SetActive(false);
        }
        if (model != null && worn == knife.Model) return;
        if (model != null) Destroy(model);
        model = ClassKnives.Make(knife);
        worn = knife.Model;
        if (model == null) return;
        var root = model.transform;
        root.SetParent(holder, false);
        // The model's own ways from its markers: the handle leads from the pommel through the grip (its origin) to the
        // blade, the edge marker lies off that line on the cutting side, the tip is how far the knife reaches.
        var pommel = Find(root, "Pommel");
        var tip = Find(root, "Tip");
        var edge = Find(root, "Edge");
        Vector3 blade = pommel != null ? -root.InverseTransformPoint(pommel.position)
            : tip != null ? root.InverseTransformPoint(tip.position) : Vector3.back;
        modelBlade = blade.sqrMagnitude > 1e-8f ? blade.normalized : Vector3.back;
        Vector3 cutting = edge != null ? Vector3.ProjectOnPlane(root.InverseTransformPoint(edge.position), modelBlade) : Vector3.zero;
        if (cutting.sqrMagnitude < 1e-8f) cutting = Vector3.ProjectOnPlane(Vector3.down, modelBlade);
        if (cutting.sqrMagnitude < 1e-8f) cutting = Vector3.ProjectOnPlane(Vector3.right, modelBlade);
        modelEdge = cutting.normalized;
        reach = (tip != null ? root.InverseTransformPoint(tip.position).magnitude : .2f) * knife.Scale;
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.Inverse(Quaternion.LookRotation(modelBlade, modelEdge));
        root.localScale = Vector3.one * knife.Scale;
        // Drawn on the body's own layer, so every view sees it as it sees the body.
        var skin = GetComponentInChildren<SkinnedMeshRenderer>();
        int layer = skin != null ? skin.gameObject.layer : gameObject.layer;
        foreach (var part in model.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = layer;
    }

    private void LateUpdate()
    {
        if (!Bind()) return;
        if (driver == null) driver = movement.GetComponent<ExecutionKnife>();
        if (eyes == null) eyes = PlayerView.EyesOf(movement);
        if (driver == null || eyes == null || !driver.Current(out var key, out var aim, out var knife))
        {
            ShowKnife(false);
            return;
        }
        if (knife != null && worn != knife.Model) Wear(knife);
        Quaternion view = eyes.rotation;
        Vector3 toPlayer = aim.Has ? Vector3.ProjectOnPlane(eyes.position - aim.Neck, Vector3.up) : Vector3.zero;
        toPlayer = toPlayer.sqrMagnitude > 1e-6f ? toPlayer.normalized : -(view * Vector3.forward);
        Vector3 neckAim = aim.Neck + toPlayer * NeckBack + Vector3.up * Mathf.Min(MostOverNeck, FistOverNeck + reach * ReachShareOverNeck);
        Vector3 headAim = aim.Head + toPlayer * HeadBack + Vector3.up * HeadAbove;
        Pose(left, key.Left, view, aim.Has, headAim);
        Pose(right, key.Right, view, aim.Has, neckAim);
        bool held = key.Held && model != null && key.Right.Weight > 0f;
        if (held) Seat(knife);
        ShowKnife(held);
    }

    private void OnDisable() => ShowKnife(false);

    private void OnDestroy()
    {
        if (holder != null) Destroy(holder.gameObject);
    }

    private bool Bind()
    {
        if (bound) return true;
        movement = GetComponentInParent<PlayerMovement>();
        animator = GetComponent<Animator>();
        if (movement == null || animator == null || !animator.isHuman) return false;
        bound = BindSide(right) && BindSide(left);
        return bound;
    }

    private bool BindSide(Side side)
    {
        side.Upper = animator.GetBoneTransform(side.Right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
        side.Lower = animator.GetBoneTransform(side.Right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
        side.Hand = animator.GetBoneTransform(side.Right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
        if (side.Upper == null || side.Lower == null || side.Hand == null) return false;
        side.TurnedCount = 0;
        side.Turned[side.TurnedCount++] = side.Upper;
        side.Turned[side.TurnedCount++] = side.Lower;
        side.Turned[side.TurnedCount++] = side.Hand;
        HumanBodyBones[] first = side.Right
            ? new[] { HumanBodyBones.RightIndexProximal, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightRingProximal, HumanBodyBones.RightLittleProximal, HumanBodyBones.RightThumbProximal }
            : new[] { HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftThumbProximal };
        for (int finger = 0; finger < 5; finger++)
            for (int joint = 0; joint < 3; joint++)
            {
                var bone = animator.GetBoneTransform(first[finger] + joint);
                side.Fingers[finger * 3 + joint] = bone;
                if (bone != null) side.Turned[side.TurnedCount++] = bone;
            }
        var middleJoint = side.Fingers[Index * 3 + 1];
        var lastJoint = side.Fingers[Index * 3 + 2];
        side.JointAxis = middleJoint != null && lastJoint != null
            ? Quaternion.Inverse(middleJoint.rotation) * (lastJoint.position - middleJoint.position).normalized
            : Vector3.forward;
        side.HandAxis = Quaternion.Inverse(side.Hand.rotation) * (side.Hand.position - side.Lower.position).normalized;
        side.ArmLength = Vector3.Distance(side.Upper.position, side.Lower.position) + Vector3.Distance(side.Lower.position, side.Hand.position);
        var knuckle = Knuckle(side);
        side.HandLength = knuckle != null ? Vector3.Distance(side.Hand.position, knuckle.position) : side.ArmLength * .17f;
        return side.ArmLength > 1e-3f && side.HandLength > 1e-4f;
    }

    // The arm where the key has it: the wrist from this shoulder in this arm's lengths, drawn toward the aim by the key's
    // share, the elbow toward its pole; the hand turned to the key's run and facing, the fingers closed; all eased by the
    // key's weight from the pose the frame had.
    private static void Pose(Side side, in KnifeStroke.Arm key, Quaternion view, bool aimed, Vector3 aim)
    {
        if (key.Weight <= 0f) return;
        for (int i = 0; i < side.TurnedCount; i++) side.Framed[i] = side.Turned[i].localRotation;
        Vector3 shoulder = side.Upper.position;
        Vector3 wrist = shoulder + view * (key.Wrist * side.ArmLength);
        if (aimed && key.Aim > 0f) wrist = Vector3.Lerp(wrist, aim, key.Aim);
        TwoBoneIK.Solve(side.Upper, side.Lower, side.Hand, wrist, shoulder + view * (key.Elbow * side.ArmLength));
        Vector3 along = view * key.Along, palm = view * key.Palm;
        TurnHand(side, along, palm);
        Fingers(side, along, palm, key.Curl, key.Thumb);
        if (key.Weight < 1f)
            for (int i = 0; i < side.TurnedCount; i++) side.Turned[i].localRotation = Quaternion.Slerp(side.Framed[i], side.Turned[i].localRotation, key.Weight);
    }

    // The knife in the right fist as the hand finally is this frame: its handle's middle at the row's place in the fist,
    // its blade and edge along the row's ways (ClassKnives' hand axes: x away from the thumb, which on a right hand is the
    // frame's -x).
    private void Seat(ClassKnives.Knife knife)
    {
        if (holder == null || knife == null) return;
        Vector3 along = AlongNow(right);
        Vector3 palm = PalmNow(right, along);
        if (along.sqrMagnitude < 1e-10f || palm.sqrMagnitude < 1e-10f) return;
        Quaternion frame = Quaternion.LookRotation(along, palm);
        Vector3 blade = frame * Mirror(knife.Blade);
        Vector3 edge = Vector3.ProjectOnPlane(frame * Mirror(knife.Edge), blade);
        if (edge.sqrMagnitude < 1e-8f) edge = Vector3.ProjectOnPlane(palm, blade);
        if (blade.sqrMagnitude < 1e-8f || edge.sqrMagnitude < 1e-8f) return;
        // A body imported at another scale (an avatar's armature at 100) must not scale the knife with it.
        float scale = Mathf.Max(1e-4f, Mathf.Abs(transform.lossyScale.x));
        holder.localScale = Vector3.one / scale;
        holder.SetPositionAndRotation(right.Hand.position + frame * Mirror(knife.Held * right.HandLength), Quaternion.LookRotation(blade, edge));
    }

    private static Vector3 Mirror(Vector3 handAxes) => new Vector3(-handAxes.x, handAxes.y, handAxes.z);

    private void ShowKnife(bool show)
    {
        if (holder != null && holder.gameObject.activeSelf != show) holder.gameObject.SetActive(show);
    }

    // The hand's rotation that lays its run (wrist to middle knuckle) along the key's and its palm facing as the key's.
    private static void TurnHand(Side side, Vector3 along, Vector3 palm)
    {
        Vector3 alongNow = AlongNow(side);
        Vector3 palmNow = PalmNow(side, alongNow);
        if (alongNow.sqrMagnitude < 1e-10f || palmNow.sqrMagnitude < 1e-10f || along.sqrMagnitude < 1e-10f || palm.sqrMagnitude < 1e-10f) return;
        side.Hand.rotation = Quaternion.LookRotation(along, palm) * Quaternion.Inverse(Quaternion.LookRotation(alongNow, palmNow)) * side.Hand.rotation;
    }

    private static Transform Knuckle(Side side) => side.Fingers[Middle * 3] != null ? side.Fingers[Middle * 3] : side.Fingers[Index * 3];

    private static Vector3 AlongNow(Side side)
    {
        var knuckle = Knuckle(side);
        return knuckle != null ? knuckle.position - side.Hand.position : side.Hand.rotation * side.HandAxis;
    }

    // Out of the palm, square to the hand's run, from the knuckles: the index's side and the little finger's side cross
    // the other way round on a right hand, so its cross is taken the other way (RepairArm's is the left's).
    private static Vector3 PalmNow(Side side, Vector3 along)
    {
        var index = side.Fingers[Index * 3];
        var outer = side.Fingers[Little * 3] != null ? side.Fingers[Little * 3] : side.Fingers[Ring * 3] != null ? side.Fingers[Ring * 3] : side.Fingers[Middle * 3];
        Vector3 palm = index != null && outer != null && outer != index
            ? (side.Right ? 1f : -1f) * Vector3.Cross(along, index.position - outer.position) : Vector3.zero;
        if (palm.sqrMagnitude < 1e-12f) palm = Vector3.down;
        palm = Vector3.ProjectOnPlane(palm, along);
        return palm.sqrMagnitude > 1e-12f ? palm.normalized : Vector3.zero;
    }

    // The four fingers closed by the key's curl, shared over their joints; the thumb across the palm and on round as far as
    // the key closes it (RepairArm.Fingers, its across mirrored for a right hand).
    private static void Fingers(Side side, Vector3 along, Vector3 palm, float curl, float thumb)
    {
        Vector3 axis = Vector3.Cross(along, palm);
        if (axis.sqrMagnitude < 1e-10f) return;
        axis.Normalize();
        for (int finger = Index; finger <= Little; finger++)
        {
            float angle = 0f;
            for (int joint = 0; joint < 3; joint++)
            {
                var bone = side.Fingers[finger * 3 + joint];
                if (bone == null) break;
                angle += curl * CurlShare[joint];
                Turn(bone, JointWay(side, finger, joint), Quaternion.AngleAxis(angle, axis) * along, 1f);
            }
        }
        float close = Mathf.Clamp01(thumb / 90f);
        if (close <= 0f || side.Fingers[Thumb * 3] == null) return;
        Vector3 over = (along * .5f + palm * .6f + (side.Right ? 1f : -1f) * axis * .45f).normalized;
        for (int joint = 0; joint < 3; joint++)
        {
            var bone = side.Fingers[Thumb * 3 + joint];
            if (bone == null) break;
            Vector3 bendAxis = Vector3.Cross(over, palm);
            if (joint > 0 && bendAxis.sqrMagnitude > 1e-8f) over = Quaternion.AngleAxis(joint == 1 ? 15f : 20f, bendAxis.normalized) * over;
            Turn(bone, JointWay(side, Thumb, joint), over, close);
        }
    }

    private static Vector3 JointWay(Side side, int finger, int joint)
    {
        var bone = side.Fingers[finger * 3 + joint];
        var next = joint < 2 ? side.Fingers[finger * 3 + joint + 1] : null;
        return next != null ? next.position - bone.position : bone.rotation * side.JointAxis;
    }

    private static void Turn(Transform bone, Vector3 now, Vector3 want, float share)
    {
        if (now.sqrMagnitude < 1e-12f || want.sqrMagnitude < 1e-12f) return;
        var turn = Quaternion.FromToRotation(now, want);
        bone.rotation = (share >= 1f ? turn : Quaternion.Slerp(Quaternion.identity, turn, share)) * bone.rotation;
    }

    private static Transform Find(Transform root, string name)
    {
        foreach (var part in root.GetComponentsInChildren<Transform>(true))
            if (part != root && string.Equals(part.name, name, System.StringComparison.OrdinalIgnoreCase)) return part;
        return null;
    }
}
