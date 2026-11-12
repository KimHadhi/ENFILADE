using System;
using System.Collections.Generic;
using MagicaCloth2;
using UnityEngine;

/// <summary>
/// What cloth can't pass through. Magica Cloth doesn't see Unity's colliders, so these are its own, measured from the
/// avatar as the body is made, so any humanoid gets them in its own size: from its bones, or for what BoothImport made,
/// from its meshes as well, the skin and clothes as they really stand (DressedBody), each shape then fitted to the cloth
/// part that rests on it (Fit). They stop cloth and nothing else: combat never
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

    [Header("The body as measured, for what BoothImport made (DressedBody)")]
    [SerializeField, Range(.5f, 1f), Tooltip("A part's outside in each direction: where this share of the points of its skin and clothes lie nearer.")]
    private float bodySurface = .9f;
    [SerializeField, Range(0f, 1f), Tooltip("A part's size among its outside's distances all round: 0.5 takes the middle one. Smaller sits inside more of the body.")]
    private float bodySize = .5f;
    [SerializeField, Range(0f, 1f), Tooltip("A shape made smaller for a cloth part lying inside it keeps at least this share of its girth, or the part meets the shape made from the bones instead.")]
    private float fitGirth = .25f;

    [Header("Chains that hang, for what BoothImport made")]
    [SerializeField, Range(.5f, 1f), Tooltip("A free bone's particle is as thick as its own cloth reaches toward the body: where this share of the cloth it carries lies nearer the bone's line.")]
    private float clothReach = .95f;
    [SerializeField, Range(0f, 2f), Tooltip("How much of the dip of a straight segment between two particles over a curve the particles add to their thickness, so the cloth between them doesn't cut into what it rests on. 0 adds none.")]
    private float curveDip = 1f;
    [SerializeField, Range(0f, 1f), Tooltip("How far a chain that hangs may turn down onto what is under it as it is made, as a share of its angle limit. 0 leaves it where its maker drew it.")]
    private float settle = 1f;
    [SerializeField, Range(0f, .2f), Tooltip("The farthest a bone may move as its chain settles onto what is under it (metres, at the model's size). What hangs farther off stays as drawn.")]
    private float settleReach = .03f;

    /// <summary>How much of a segment's dip over a curve the particles at its ends add (ClothBones.FitRadii).</summary>
    internal float CurveDip => curveDip;
    /// <summary>How far, as a share of the angle limit, a hanging chain may turn down onto the body (ClothBones.Settle).</summary>
    internal float SettleShare => settle;
    /// <summary>The farthest a bone may move as its chain settles (metres at the model's size).</summary>
    internal float SettleReach => settleReach;

    // The sets made from the bones are kept by name, the ones measured from the meshes by name and this mark.
    private const string Measured = "+";

    private readonly Dictionary<string, List<ColliderComponent>> made =
        new Dictionary<string, List<ColliderComponent>>(StringComparer.OrdinalIgnoreCase);
    // Smaller copies of the shapes above for parts that hang inside them, by the shape and how much smaller (mm). Null
    // where the copy would be too thin to keep.
    private readonly Dictionary<(ColliderComponent, int), ColliderComponent> fitted =
        new Dictionary<(ColliderComponent, int), ColliderComponent>();
    // A measured shape's twin made from the bones, which a part that can't take the measured one meets instead.
    private readonly Dictionary<ColliderComponent, ColliderComponent> fromBones = new Dictionary<ColliderComponent, ColliderComponent>();
    // The chest's soft lumps as balls, and the smaller copies made of them: the shapes that lie on the body's real surface,
    // which a hanging chain may settle onto (ClothBones.Settle).
    private readonly HashSet<ColliderComponent> lumps = new HashSet<ColliderComponent>();
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
    /// for, so two parts that touch the legs share one set. The arms make none: they press instead. With
    /// <paramref name="measured"/> the shapes are the body's real ones, measured from its meshes, wherever it could be
    /// measured.
    /// </summary>
    internal List<ColliderComponent> For(Animator animator, string shapes, DressedBody measured = null)
    {
        Use(animator);
        var found = new List<ColliderComponent>();
        foreach (string shape in shapes.Split(ShapeSeparator))
        {
            string name = shape.Trim();
            if (!string.Equals(name, Arms, StringComparison.OrdinalIgnoreCase)) found.AddRange(Set(animator, name, measured));
        }
        return found;
    }

    /// <summary>
    /// The body's real shapes, measured from its meshes (DressedBody) with this body's numbers; each soft lump on the
    /// chest (<paramref name="springs"/>: a bust) measured as a ball of its own.
    /// </summary>
    internal DressedBody Measure(Animator animator, string look, Func<SkinSample> read, HashSet<Transform> hanging, HashSet<Transform> springs) =>
        DressedBody.Measure(animator, look, read, hanging, springs, bodySurface, bodySize, clothReach);

    /// <summary>
    /// Gives this component ClothBody.csv's numbers where it has no player to bind them (the menu's preview), its own
    /// columns only: the rest belong to PlayerBodyCloth.
    /// </summary>
    internal void ReadTable()
    {
        if (!GameTables.TryGetRow(PlayerBodyCloth.TableName, PlayerBodyCloth.TableRow, out var row)) return;
        foreach (var cell in row) TableFieldWriter.TryWrite(this, cell.Key, cell.Value, out _);
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
        foreach (var shape in shapes) least = Mathf.Min(least, Gap(point, shape));
        return least;
    }

    /// <summary>
    /// Room, among these colliders, to the chest's soft lumps only (a bust): the shapes measured to lie on the body's real
    /// surface (DressedBody.Dome). The rods and other balls are sized from the middle of their outside and run some
    /// millimetres inside it here and there, so a chain laid on them would sink into the body (Mayo's leg ribbons 20 mm
    /// into her thigh). Infinity when there are none.
    /// </summary>
    internal float RoomOnLumps(Vector3 point, List<ColliderComponent> shapes)
    {
        float least = float.PositiveInfinity;
        foreach (var shape in shapes)
            if (shape != null && lumps.Contains(shape)) least = Mathf.Min(least, Gap(point, shape));
        return least;
    }

    /// <summary>
    /// Fits a part's shapes (as For gave them) to its cloth as the body was made. These shapes are rough, and Magica
    /// pushes cloth out of any it is in, so a piece its maker drew close against the body (a choker round the neck, a
    /// necklace on the chest, ears rising from the head) would be shoved off what the maker drew before anything moved.
    /// A shape the cloth comes nearer than clear to (the cloth's thinnest: as near as it can be without a push) is
    /// swapped for a copy that much smaller, which every part needing the same shares; one that would keep less than
    /// fitGirth of its girth goes. A measured shape made smaller no longer covers all the body it stood for (a tail
    /// grows out of the hips' ball, Ichigo's took it from 99 mm to 53), so its twin made from the bones, fitted the same
    /// way, joins it and covers what it can of the rest. The cloth still meets the body, just where its own shape says
    /// the body is. <paramref name="roots"/>: the bones it hangs from count (a ring, colliding line by line).
    /// </summary>
    internal void Fit(List<ColliderComponent> shapes, ClothBones bones, float clear, bool roots)
    {
        for (int i = shapes.Count - 1; i >= 0; i--)
        {
            var shape = shapes[i];
            if (shape == null || shape is MagicaPlaneCollider) continue;
            var kept = Fitted(shape, bones, clear, roots);
            var twin = kept != shape && fromBones.TryGetValue(shape, out var boneMade) ? Fitted(boneMade, bones, clear, roots) : null;
            if (kept == null) (kept, twin) = (twin, null);
            if (kept == null)
            {
                shapes.RemoveAt(i);
                continue;
            }
            shapes[i] = kept;
            // Added past this one, so the loop, walking down, never meets it.
            if (twin != null && twin != kept) shapes.Add(twin);
        }
    }

    // The shape itself, a copy smaller by what the cloth needs, or null where that leaves too little.
    private ColliderComponent Fitted(ColliderComponent shape, ClothBones bones, float clear, bool roots)
    {
        float shrink = clear - bones.Nearest(point => Gap(point, shape), roots);
        return shrink <= 0f ? shape : Smaller(shape, shrink);
    }

    /// <summary>
    /// When the shape nearest a point among these is one of the chest's soft lumps (they lie on the body's real surface),
    /// its rounding: the curve cloth lying there wraps round. Below zero when the nearest is another shape, whose middle
    /// size runs some millimetres inside the body here and there: a particle there keeps its maker's thickness, which held
    /// it off the body that much (thinner, Ichigo's small twin tails sank 12 mm deeper when pressed).
    /// </summary>
    internal float LumpRounding(Vector3 point, List<ColliderComponent> shapes)
    {
        ColliderComponent nearest = null;
        float least = float.PositiveInfinity;
        foreach (var shape in shapes)
        {
            float gap = Gap(point, shape);
            if (gap >= least) continue;
            least = gap;
            nearest = shape;
        }
        return nearest != null && lumps.Contains(nearest) ? Mathf.Abs(nearest.GetScale()) * nearest.GetSize().x : -1f;
    }

    // A copy of a shape on the same bone, its rounding thinner by at least this much (whole millimetres, so near needs
    // share one); a capsule keeps the line it runs along.
    private ColliderComponent Smaller(ColliderComponent shape, float shrink)
    {
        int mm = Mathf.CeilToInt(shrink * 1000f);
        if (fitted.TryGetValue((shape, mm), out var copy)) return copy;
        float scale = Mathf.Abs(shape.GetScale()), by = mm / 1000f;
        Vector3 size = shape.GetSize() * scale;
        Transform at = shape.transform;
        if (shape is MagicaCapsuleCollider capsule)
        {
            float start = size.x - by, end = size.y - by;
            if (start < size.x * fitGirth || end < size.y * fitGirth) return fitted[(shape, mm)] = null;
            var thinner = Shape<MagicaCapsuleCollider>(at.parent, at.position, at.rotation);
            thinner.direction = capsule.direction;
            thinner.alignedOnCenter = capsule.alignedOnCenter;
            thinner.reverseDirection = capsule.reverseDirection;
            thinner.SetSize(start / scale, end / scale, (size.z - 2f * by) / scale);
            copy = thinner;
        }
        else
        {
            float radius = size.x - by;
            if (radius < size.x * fitGirth) return fitted[(shape, mm)] = null;
            var thinner = Shape<MagicaSphereCollider>(at.parent, at.position, at.rotation);
            thinner.SetSize(radius / scale);
            copy = thinner;
        }
        copy.center = shape.center;
        copy.name = "Cloth collider (fitted)";
        if (lumps.Contains(shape)) lumps.Add(copy);
        return fitted[(shape, mm)] = copy;
    }

    /// <summary>How far a point is outside one collider (metres, below zero inside). Infinity for the floor.</summary>
    internal static float Gap(Vector3 point, ColliderComponent shape)
    {
        if (shape == null || shape is MagicaPlaneCollider) return float.PositiveInfinity;
        float scale = Mathf.Abs(shape.GetScale());
        Vector3 size = shape.GetSize() * scale, start = shape.transform.TransformPoint(shape.center);
        if (shape is not MagicaCapsuleCollider) return Vector3.Distance(point, start) - size.x;
        // As Capsule makes it: down the object's -Y, the length counting both ends' rounding.
        Vector3 axis = -shape.transform.up * Mathf.Max(0f, size.z - size.x - size.y);
        float along = axis.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(point - start, axis) / axis.sqrMagnitude) : 0f;
        return Vector3.Distance(point, start + axis * along) - Mathf.Lerp(size.x, size.y, along);
    }

    // Everything made is for one body: a new one starts over, the old one's colliders gone with it.
    private void Use(Animator animator)
    {
        if (animator == madeFor) return;
        made.Clear();
        fitted.Clear();
        fromBones.Clear();
        lumps.Clear();
        arms = null;
        madeFor = animator;
    }

    private List<ColliderComponent> Set(Animator animator, string shape, DressedBody measured)
    {
        if (measured == null) return Set(animator, shape);
        if (made.TryGetValue(shape + Measured, out var set)) return set;
        // Each part as measured where the meshes gave it, beside its twin from the bones in that set, which may join it
        // (Fit); the rest is that set's own shape.
        var bones = Set(animator, shape);
        set = new List<ColliderComponent>(bones);
        var parts = string.Equals(shape, Legs, StringComparison.OrdinalIgnoreCase) ? LegParts
            : string.Equals(shape, Head, StringComparison.OrdinalIgnoreCase) ? HeadParts : Array.Empty<DressedBody.Part>();
        for (int i = 0; i < parts.Length && i < set.Count; i++)
        {
            var real = MakeMeasured(measured, parts[i]);
            if (real == null) continue;
            fromBones[real] = set[i];
            set[i] = real;
        }
        // The chest's soft lumps (a bust) as balls where they rest on the chest (DressedBody), so what hangs over them
        // rests on them. The chest's rod is measured without them: a rod can't be round the ribs and out to the front of
        // a bust both, and on Mayo it ran some 4 cm inside her bust, as far as her side locks sank.
        if (string.Equals(shape, Head, StringComparison.OrdinalIgnoreCase))
            for (int i = 0; i < measured.LumpCount; i++)
                if (measured.Lump(i, out var bone, out var centre, out float radius))
                {
                    Ball(bone, centre, radius, set);
                    lumps.Add(set[set.Count - 1]);
                }
        return made[shape + Measured] = set;
    }

    // The parts of each set in the order the bones make them (MakeLegs, MakeHead): the floor comes after the legs.
    private static readonly DressedBody.Part[] LegParts =
    {
        DressedBody.Part.Pelvis, DressedBody.Part.LeftThigh, DressedBody.Part.RightThigh, DressedBody.Part.LeftShin, DressedBody.Part.RightShin
    };
    private static readonly DressedBody.Part[] HeadParts = { DressedBody.Part.Head, DressedBody.Part.Chest };

    // One part of the body as its meshes measured it, or null where they couldn't.
    private static ColliderComponent MakeMeasured(DressedBody measured, DressedBody.Part part)
    {
        var one = new List<ColliderComponent>(1);
        if (part is DressedBody.Part.Head or DressedBody.Part.Pelvis)
        {
            if (measured.Ball(part, out var bone, out var centre, out float radius)) Ball(bone, centre, radius, one);
        }
        else if (measured.Rod(part, out var bone, out var a, out var b, out float startRadius, out float endRadius))
            Capsule(bone, a, b, startRadius, endRadius, one);
        return one.Count > 0 ? one[0] : null;
    }

    private List<ColliderComponent> Set(Animator animator, string shape)
    {
        if (made.TryGetValue(shape, out var set)) return set;
        set = new List<ColliderComponent>();
        if (string.Equals(shape, Legs, StringComparison.OrdinalIgnoreCase)) MakeLegs(animator, set);
        else if (string.Equals(shape, Head, StringComparison.OrdinalIgnoreCase)) MakeHead(animator, set);
        return made[shape] = set;
    }

    // A rig whose humanoid finds no bones (the avatar let go: an outfit's bone of the same name under one of the body's,
    // before OutfitDresser renamed them) gets no colliders rather than an error that stops the whole cloth.
    private void MakeLegs(Animator animator, List<ColliderComponent> into)
    {
        Transform leftLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg), rightLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        Transform leftKnee = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg), rightKnee = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        if (leftLeg == null || rightLeg == null || leftKnee == null || rightKnee == null || animator.GetBoneTransform(HumanBodyBones.Hips) == null
            || animator.GetBoneTransform(HumanBodyBones.LeftFoot) == null || animator.GetBoneTransform(HumanBodyBones.RightFoot) == null) return;
        Ball(animator.GetBoneTransform(HumanBodyBones.Hips), (leftLeg.position + rightLeg.position) * .5f,
            Vector3.Distance(leftLeg.position, rightLeg.position) * pelvis, into);
        Limb(leftLeg, leftKnee, thighStart, thighEnd, into);
        Limb(rightLeg, rightKnee, thighStart, thighEnd, into);
        Limb(leftKnee, animator.GetBoneTransform(HumanBodyBones.LeftFoot), shinStart, shinEnd, into);
        Limb(rightKnee, animator.GetBoneTransform(HumanBodyBones.RightFoot), shinStart, shinEnd, into);
        var ground = MakeFloor();
        if (ground != null) into.Add(ground);
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
        Transform leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), rightArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        if (head == null || upperBody == null || leftArm == null || rightArm == null) return;
        float shoulders = Vector3.Distance(leftArm.position, rightArm.position);
        Vector3 up = (head.position - neck.position).normalized;
        Ball(head, head.position + up * (shoulders * headLift), shoulders * headRadius, into);
        Capsule(upperBody, neck, shoulders * chest, shoulders * chest, into);
    }

    // The floor is only a plane under the capsule, which keeps a slide's cloth out of the ground. A body with no player
    // under it (the menu's preview) stands still and gets none.
    private MagicaPlaneCollider MakeFloor()
    {
        if (floor == null)
        {
            capsule = GetComponent<CharacterController>();
            player = GetComponent<PlayerMovement>();
            if (capsule == null || player == null) return null;
            floor = new GameObject("Cloth floor", typeof(MagicaPlaneCollider)).GetComponent<MagicaPlaneCollider>();
            floor.transform.SetParent(transform, false);
        }
        return floor;
    }

    private void LateUpdate()
    {
        if (floor == null || player == null) return;
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

    private static void Capsule(Transform from, Transform to, float start, float end, List<ColliderComponent> into) =>
        Capsule(from, from.position, to.position, start, end, into);

    // On the bone, from one point to the other. Aligned to its start, Magica's capsule runs down the object's -Y, and
    // its length counts both ends' rounding.
    private static void Capsule(Transform bone, Vector3 a, Vector3 b, float start, float end, List<ColliderComponent> into)
    {
        var shape = Shape<MagicaCapsuleCollider>(bone, a, Quaternion.FromToRotation(Vector3.down, b - a));
        float scale = Mathf.Abs(shape.GetScale());
        shape.direction = MagicaCapsuleCollider.Direction.Y;
        shape.alignedOnCenter = false;
        shape.SetSize(start / scale, end / scale, (Vector3.Distance(a, b) + start + end) / scale);
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
