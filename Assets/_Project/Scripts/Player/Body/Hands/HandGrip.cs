using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fits all five fingers to the held surface. Measures the character's phalanges and skin at rest, then closes
/// each anatomical hinge only as far as its padded segment can go without entering the support.
/// </summary>
internal sealed partial class HandGrip
{
    private sealed class Finger
    {
        public readonly Transform[] Bones = new Transform[3];
        public readonly Vector3[] Axes = new Vector3[3];
        public readonly float[] Lengths = new float[3], Radii = new float[3];
        public readonly Quaternion[] Before = new Quaternion[3], Rest = new Quaternion[3];
        public Vector3 OpenInHand;
        public bool Thumb;
        // Which finger: 0 index, 1 middle, 2 ring, 3 little, 4 thumb (a hand may lack one, so not its place in the list).
        public int Digit;
        public SkinVertex[] Skin;
        public Transform[] SkinBones;
        public Matrix4x4[] SkinMatrices;
    }

    private readonly struct SkinPoint
    {
        public readonly Vector3 Position;
        private readonly BoneWeight weights;
        private readonly Transform[] bones;
        private readonly Matrix4x4[] bindposes;
        public SkinPoint(Vector3 position, BoneWeight weights, Transform[] bones, Matrix4x4[] bindposes)
        { Position = position; this.weights = weights; this.bones = bones; this.bindposes = bindposes; }
        public float WeightOn(Transform bone)
            => (bones[weights.boneIndex0] == bone ? weights.weight0 : 0f) + (bones[weights.boneIndex1] == bone ? weights.weight1 : 0f)
                + (bones[weights.boneIndex2] == bone ? weights.weight2 : 0f) + (bones[weights.boneIndex3] == bone ? weights.weight3 : 0f);
        public SkinVertex Vertex()
        {
            var indices = new[] { weights.boneIndex0, weights.boneIndex1, weights.boneIndex2, weights.boneIndex3 };
            var amounts = new Vector4(weights.weight0, weights.weight1, weights.weight2, weights.weight3);
            var blend = Matrix4x4.zero;
            for (int i = 0; i < 4; i++)
            {
                if (amounts[i] <= 0f) continue;
                Matrix4x4 matrix = bones[indices[i]].localToWorldMatrix * bindposes[indices[i]];
                for (int cell = 0; cell < 16; cell++) blend[cell] += matrix[cell] * amounts[i];
            }
            Vector3 original = blend.inverse.MultiplyPoint3x4(Position);
            return new SkinVertex(bones, bindposes, indices, amounts, original);
        }
    }

    // A small set of the actual weighted skin vertices, evaluated directly as the finger moves. No BakeMesh in play.
    private sealed class SkinVertex
    {
        private readonly Transform[] bones = new Transform[4];
        private readonly Vector3[] local = new Vector3[4];
        private readonly int[] indices = new int[4];
        private readonly Vector4 weights;
        public SkinVertex(Transform[] source, Matrix4x4[] bindposes, int[] indices, Vector4 weights, Vector3 original)
        {
            this.weights = weights;
            for (int i = 0; i < 4; i++) { bones[i] = source[indices[i]]; local[i] = bindposes[indices[i]].MultiplyPoint3x4(original); }
        }
        public void Bind(List<Transform> transforms)
        {
            for (int i = 0; i < 4; i++)
            {
                if (weights[i] <= 0f) continue;
                int index = transforms.IndexOf(bones[i]);
                if (index < 0) { index = transforms.Count; transforms.Add(bones[i]); }
                indices[i] = index;
            }
        }
        public Vector3 Position(Matrix4x4[] matrices)
        {
            Vector3 point = Vector3.zero;
            for (int i = 0; i < 4; i++) if (weights[i] > 0f) point += matrices[indices[i]].MultiplyPoint3x4(local[i]) * weights[i];
            return point;
        }
    }

    private readonly Transform hand;
    private readonly Animator animator;
    private readonly bool left;
    private HandSkinCorrection normals;
    private readonly Finger[] fingers;
    private readonly Finger thumb;
    private readonly Quaternion[] relaxedThumb = new Quaternion[3];
    private readonly Vector3 forwardInHand, palmInHand;
    private readonly Quaternion[] barPose, ledgePose, displayedLedgePose;
    private bool barCached, ledgeCached;
    private bool ledgeDisplayed;
    private Vector4 barSettings;
    private Vector3 ledgeMin, ledgeMax;
    private Quaternion ledgeFrame;
    private int ledgeShape;
    private float ledgeClearance;
    public Vector3 PalmLocal { get; }
    public float PalmDepth { get; }
    public float HalfWidth { get; }

    private HandGrip(Transform hand, Finger[] fingers, Vector3 palmLocal, float depth, float halfWidth, Vector3 forward, Vector3 palm, Animator animator, bool left)
    {
        this.hand = hand; this.fingers = fingers;
        this.animator = animator; this.left = left;
        PalmLocal = palmLocal; PalmDepth = depth; HalfWidth = halfWidth;
        forwardInHand = hand.InverseTransformDirection(forward);
        palmInHand = hand.InverseTransformDirection(palm);
        barPose = new Quaternion[fingers.Length * 3];
        ledgePose = new Quaternion[fingers.Length * 3];
        displayedLedgePose = new Quaternion[fingers.Length * 3];
        thumb = System.Array.Find(fingers, finger => finger.Thumb);
        if (thumb != null) System.Array.Copy(thumb.Rest, relaxedThumb, 3);
    }

    /// <summary>
    /// The thumb at ease, measured from this hand at rest: turned under the palm about the line of the fingers until it sits
    /// <paramref name="underPalm"/> degrees out of the palm's plane, with its outer joints bent <paramref name="bend"/> degrees
    /// toward the palm. A thumb the model already holds further under stays there. Clips can't be trusted with this: retargeting
    /// carries a thumb from one character's rest pose to another's, and it comes out standing off the hand.
    /// </summary>
    public void PrepareThumb(float underPalm, float bend)
    {
        if (thumb == null) return;
        Vector3 forward = hand.TransformDirection(forwardInHand), palm = hand.TransformDirection(palmInHand);
        Transform root = thumb.Bones[0];
        Vector3 apart = Vector3.ProjectOnPlane(thumb.Bones[1].position - root.position, forward);
        Vector3 side = Vector3.ProjectOnPlane(apart, palm);
        if (side.sqrMagnitude < 1e-8f) return;
        Vector3 axis = Vector3.Cross(side, palm).normalized;
        float turn = underPalm - Vector3.SignedAngle(side, apart, axis);
        if (turn > 0f) root.rotation = Quaternion.AngleAxis(turn, axis) * root.rotation;
        Vector3 centre = hand.TransformPoint(PalmLocal);
        for (int i = 1; i < 3; i++)
        {
            var bone = thumb.Bones[i];
            Vector3 hinge = Vector3.Cross(bone.TransformDirection(thumb.Axes[i]), centre - bone.position);
            if (hinge.sqrMagnitude > 1e-10f) bone.rotation = Quaternion.AngleAxis(bend, hinge.normalized) * bone.rotation;
        }
        for (int i = 0; i < 3; i++)
        {
            relaxedThumb[i] = thumb.Bones[i].localRotation;
            thumb.Bones[i].localRotation = thumb.Rest[i];
        }
    }

    public void RelaxThumb(float weight)
    {
        if (thumb == null || weight <= 0f) return;
        for (int i = 0; i < 3; i++)
            thumb.Bones[i].localRotation = Quaternion.Slerp(thumb.Bones[i].localRotation, relaxedThumb[i], weight);
    }

    public static HandGrip Create(Animator animator, bool left, Vector3 forward, Vector3 palm)
    {
        var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        var middle = animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
        float palmLength = middle != null ? Vector3.Distance(middle.position, hand.position) : .07f;
        Vector3 across = Vector3.Cross(forward, palm).normalized;
        var skin = SkinNear(animator, hand.position, palmLength * 2.5f);
        var result = new List<Finger>(5);
        Vector3 knuckles = Vector3.zero;
        int knuckleCount = 0;

        void Add(HumanBodyBones first, int digit, bool thumb = false)
        {
            var finger = new Finger { Thumb = thumb, Digit = digit };
            for (int i = 0; i < 3; i++) finger.Bones[i] = animator.GetBoneTransform((HumanBodyBones)((int)first + i));
            if (finger.Bones[0] == null || finger.Bones[1] == null || finger.Bones[2] == null) return;
            Vector3 previous = finger.Bones[1].position - finger.Bones[0].position;
            if (previous.sqrMagnitude < 1e-8f) return;
            finger.OpenInHand = hand.InverseTransformDirection(previous.normalized);
            for (int i = 0; i < 3; i++)
            {
                var bone = finger.Bones[i];
                finger.Rest[i] = bone.localRotation;
                Vector3 segment = i < 2 ? finger.Bones[i + 1].position - bone.position : previous * .75f;
                if (i == 2 && bone.childCount > 0 && (bone.GetChild(0).position - bone.position).sqrMagnitude > 1e-8f)
                    segment = bone.GetChild(0).position - bone.position;
                float length = segment.magnitude;
                if (length < .001f) return;
                Vector3 direction = segment / length;
                float radius = SkinRadius(skin, bone, direction, length);
                if (i == 2)
                {
                    // No fingertip bone is required: measure the skin's end, less the fingertip pad.
                    float tip = 0f;
                    foreach (var vertex in skin)
                    {
                        if (vertex.WeightOn(bone) < .3f) continue;
                        Vector3 delta = vertex.Position - bone.position;
                        float along = Vector3.Dot(delta, direction);
                        if (along > 0f && along < previous.magnitude * 1.6f
                            && (delta - direction * along).sqrMagnitude < radius * radius * 2.25f) tip = Mathf.Max(tip, along);
                    }
                    if (tip > radius + .002f) length = tip - radius;
                }
                finger.Axes[i] = bone.InverseTransformDirection(direction);
                finger.Lengths[i] = length;
                finger.Radii[i] = radius;
                previous = segment;
            }
            finger.Skin = SkinSamples(skin, finger, palm);
            var transforms = new List<Transform>();
            foreach (var vertex in finger.Skin) vertex.Bind(transforms);
            finger.SkinBones = transforms.ToArray();
            finger.SkinMatrices = new Matrix4x4[transforms.Count];
            if (!thumb) { knuckles += finger.Bones[0].position; knuckleCount++; }
            result.Add(finger);
        }

        Add(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal, 0);
        Add(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal, 1);
        Add(left ? HumanBodyBones.LeftRingProximal : HumanBodyBones.RightRingProximal, 2);
        Add(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal, 3);
        Add(left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal, 4, true);

        Vector3 centre = hand.position + (knuckleCount > 0 ? knuckles / knuckleCount - hand.position : forward * palmLength) * .55f;
        float depth = palmLength * .08f, halfWidth = palmLength * .35f;
        foreach (var vertex in skin)
        {
            Vector3 fromWrist = vertex.Position - hand.position;
            float along = Vector3.Dot(fromWrist, forward), side = Mathf.Abs(Vector3.Dot(fromWrist, across));
            if (along > palmLength * .2f && along < palmLength * .85f && side < palmLength * .4f)
                depth = Mathf.Max(depth, Vector3.Dot(vertex.Position - centre, palm));
        }
        foreach (var finger in result)
        {
            Vector3 baseOffset = finger.Bones[0].position - centre;
            depth = Mathf.Max(depth, Vector3.Dot(baseOffset, palm) + finger.Radii[0]);
            if (!finger.Thumb) halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(baseOffset, across)) + finger.Radii[0]);
        }
        // The thumb web's skin correction needs its own copy of the body mesh; made now, not on the first ledge. A skin it
        // can't measure goes without it: the hand itself must still be made.
        foreach (var finger in result)
            if (finger.Thumb)
                try { HandSkinCorrection.For(animator).CopyAhead(finger.Bones); }
                catch (System.Exception error) { Debug.LogWarning($"HandGrip: the thumb-web skin correction is off on {animator.name}: {error.Message}", animator); }
        return new HandGrip(hand, result.ToArray(), hand.InverseTransformPoint(centre), Mathf.Clamp(depth, .002f, palmLength * .65f), halfWidth, forward, palm, animator, left);
    }

    private static List<SkinPoint> SkinNear(Animator animator, Vector3 centre, float reach)
    {
        var points = new List<SkinPoint>();
        var baked = new Mesh { name = "Hand contact measurement", hideFlags = HideFlags.HideAndDontSave };
        foreach (var renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            // A mesh without Read/Write gives no bone weights (0.7.34 skipped it the same way, by their count).
            if (renderer.sharedMesh == null || !renderer.sharedMesh.isReadable
                || (renderer.bounds.ClosestPoint(centre) - centre).sqrMagnitude > reach * reach) continue;
            // Compensate renderer scale here; TransformPoint applies the character's fitted scale once below.
            renderer.BakeMesh(baked, true);
            var vertices = baked.vertices;
            var weights = renderer.sharedMesh.boneWeights;
            var bones = renderer.bones;
            var bindposes = renderer.sharedMesh.bindposes;
            if (weights.Length != vertices.Length || bones.Length == 0) continue;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 point = renderer.transform.TransformPoint(vertices[i]);
                if ((point - centre).sqrMagnitude < reach * reach) points.Add(new SkinPoint(point, weights[i], bones, bindposes));
            }
            baked.Clear();
        }
        if (Application.isPlaying) Object.Destroy(baked); else Object.DestroyImmediate(baked);
        return points;
    }

    private static float SkinRadius(List<SkinPoint> skin, Transform bone, Vector3 direction, float length)
    {
        float fallback = Mathf.Clamp(length * .22f, .0025f, .008f), radius = 0f;
        foreach (var vertex in skin)
        {
            if (vertex.WeightOn(bone) < .3f) continue;
            Vector3 delta = vertex.Position - bone.position;
            float along = Vector3.Dot(delta, direction);
            if (along < length * .15f || along > length * .85f) continue;
            float radial = (delta - direction * along).magnitude;
            if (radial < fallback * 1.6f) radius = Mathf.Max(radius, radial);
        }
        return radius > .001f ? radius : fallback;
    }

    private static SkinVertex[] SkinSamples(List<SkinPoint> skin, Finger finger, Vector3 palm)
    {
        var selected = new HashSet<int>();
        for (int joint = 0; joint < 3; joint++)
        {
            var bone = finger.Bones[joint];
            Vector3 along = bone.TransformDirection(finger.Axes[joint]), side = Vector3.Cross(along, palm).normalized;
            for (int direction = 0; direction < 26; direction++)
            {
                float angle = (direction % 8) * Mathf.PI * .25f;
                Vector3 axis = direction < 24 ? palm * Mathf.Cos(angle) + side * Mathf.Sin(angle) + along * ((direction / 8 - 1) * .7f)
                    : along * (direction == 24 ? 1f : -1f);
                float best = float.NegativeInfinity;
                int index = -1;
                for (int vertex = 0; vertex < skin.Count; vertex++)
                {
                    if (skin[vertex].WeightOn(bone) < .1f) continue;
                    float score = Vector3.Dot(skin[vertex].Position - bone.position, axis);
                    if (score > best) { best = score; index = vertex; }
                }
                if (index >= 0) selected.Add(index);
            }
        }
        var result = new List<SkinVertex>(selected.Count);
        foreach (int index in selected) result.Add(skin[index].Vertex());
        return result.ToArray();
    }

    public void PrepareBar(float halfThickness, float palmClearance, float clearance, float sectionAngle = 0f)
    {
        Vector4 settings = new Vector4(halfThickness, palmClearance, clearance, sectionAngle);
        if (!barCached || (barSettings - settings).sqrMagnitude > 1e-10f)
        {
            // Keep the square's real faces when the hand wraps a rail corner at an angle.
            Vector3 forward = hand.TransformDirection(forwardInHand), palm = hand.TransformDirection(palmInHand);
            Quaternion section = Quaternion.AngleAxis(sectionAngle, Vector3.Cross(forward, palm));
            Vector3 sectionForward = section * forward, sectionPalm = section * palm;
            float depth = halfThickness * (Mathf.Abs(Vector3.Dot(palm, sectionForward)) + Mathf.Abs(Vector3.Dot(palm, sectionPalm)));
            Vector3 centre = hand.TransformPoint(PalmLocal) + palm * (depth + PalmDepth + palmClearance);
            // Cover the skin triangles between pad samples when they wrap a sharp corner.
            float cornerPadding = Mathf.Abs(Mathf.Sin(sectionAngle * 2f * Mathf.Deg2Rad)) * .002f;
            SolvePose(new Contact(centre, sectionForward, sectionPalm, halfThickness), forward, palm, clearance + cornerPadding, barPose);
            barSettings = settings;
            barCached = true;
        }
    }

    public void Bar(float halfThickness, float palmClearance, float clearance, float weight, float curl, float sectionAngle = 0f)
    {
        if (weight <= 0f) return;
        PrepareBar(halfThickness, palmClearance, clearance, sectionAngle);
        ApplyPose(barPose, weight, curl);
    }

    public void Ledge(Collider surface, Vector3 forward, Vector3 palm, Vector3 wrist, Quaternion rotation, float clearance, float weight)
    {
        if (weight <= 0f) { ClearLedgeSkin(); return; }
        var contact = new Contact(surface, forward, palm);
        Vector3 originalPosition = hand.localPosition;
        Quaternion originalRotation = hand.localRotation;
        try
        {
            // Evaluate at the intended contact, not the animated wrist's tracking error. Looking around must not
            // change the geometry key or make a finger choose another branch of the contact search.
            hand.SetPositionAndRotation(wrist, rotation);
            contact.Key(hand, HalfWidth + .15f, out Vector3 min, out Vector3 max, out Quaternion frame, out int shape);
            if (!ledgeCached || shape != ledgeShape || Mathf.Abs(clearance - ledgeClearance) > 1e-7f || (min - ledgeMin).sqrMagnitude > 2.5e-7f
                || (max - ledgeMax).sqrMagnitude > 2.5e-7f || Quaternion.Angle(frame, ledgeFrame) > .2f)
            {
                SolvePose(contact, forward, palm, clearance, ledgePose);
                ledgeMin = min; ledgeMax = max; ledgeFrame = frame; ledgeShape = shape;
                ledgeClearance = clearance;
                ledgeCached = true;
            }
        }
        finally
        {
            // The temporary contact frame is only for the finger calculation; never move or stretch the wrist.
            hand.localPosition = originalPosition;
            hand.localRotation = originalRotation;
        }
        if (!ledgeDisplayed) { System.Array.Copy(ledgePose, displayedLedgePose, ledgePose.Length); ledgeDisplayed = true; }
        float follow = Time.deltaTime > 0f ? 1f - Mathf.Exp(-Time.deltaTime / .06f) : 1f;
        for (int i = 0; i < ledgePose.Length; i++) displayedLedgePose[i] = Quaternion.Slerp(displayedLedgePose[i], ledgePose[i], follow);
        ApplyPose(displayedLedgePose, weight);
        normals.OrNull()?.SetWeight(left, weight);
    }

    public void ClearLedgeSkin() => normals.OrNull()?.SetWeight(left, 0f);

    /// <summary>The way the palm faces, as measured on this hand.</summary>
    public Vector3 PalmFacing => hand.TransformDirection(palmInHand);

    // How far a finger's joints bend, in degrees, from straight along the joint before (+ toward the palm): knuckle, middle,
    // tip. Past this a hand is broken, not gripping. KINEMATION's own grips bend a knuckle up to 103 (the AK's ring finger
    // round its grip) and press fingertips back up to 36 under a handguard (Mk14 EBR, MX68), measured 2026-11-12.
    private static readonly Vector2[] HumanBend = { new Vector2(-25f, 105f), new Vector2(-5f, 110f), new Vector2(-40f, 90f) };
    // How far a joint swings sideways out of its bend (degrees): the knuckle spreads a little, the others hardly.
    private const float KnuckleSide = 25f, JointSide = 12f;

    // The thumb, against its own rest turn in the joint before (it doesn't bend toward the palm the way the fingers do): how
    // far each joint may swing from its rest, base to tip (degrees), and how far any joint may twist along itself, the
    // fingers' too.
    private static readonly float[] ThumbSwing = { 75f, 85f, 90f };
    private const float MostTwist = 40f;

    /// <summary>
    /// Brings every finger joint bent past what a hand can do back within a hand's reach, after everything else has posed
    /// the fingers. KINEMATION's finger muscles run to ±5 against a humanoid's ±1, and on a body whose avatar sets its
    /// fingers up differently (Sil, whose avatar is mapped on import) they broke the hand holding a gun, the support hand's
    /// thumb standing off it backwards (user's photo, 2026-11-09). The fingers are measured from the bones' own directions
    /// about the palm; the thumb, which opposes the palm instead, against its rest; no joint twists past MostTwist. So it
    /// holds on any humanoid, a mod's character included.
    /// Each finger's joints bend about one hinge, across the way the open finger points and the way the palm faces, as a
    /// finger's joints do and as the shapes bend them (SolveShape). The hinge was taken afresh at each joint from the segment
    /// before it, and once a finger had curled past 90 degrees that hinge turned the other way: every bend after it read as
    /// bent backwards and was "kept" straight. The shapes curl the trigger hand's middle, ring and little fingers about 170
    /// degrees by the middle joint (Trigger) and the support hand's 110 (Cradle), so those fingertips were opened 25 to 50
    /// degrees every frame (Trigger's from 55 to 5) and stood off the gun (worked through 2026-11-12; the user: "그냥 손
    /// 잡는 손이 이상해").
    /// </summary>
    public void KeepHuman()
    {
        if (thumb != null)
            for (int i = 0; i < 3; i++) KeepFromRest(thumb, i, ThumbSwing[i]);
        Vector3 palm = PalmFacing.normalized;
        foreach (var finger in fingers)
        {
            if (finger.Thumb) continue;
            for (int i = 0; i < 3; i++) KeepFromRest(finger, i, 180f);
            Vector3 incoming = hand.TransformDirection(finger.OpenInHand).normalized;
            Vector3 hinge = Vector3.Cross(incoming, palm);
            if (hinge.sqrMagnitude < 1e-6f) continue;
            hinge.Normalize();
            for (int i = 0; i < 3; i++)
            {
                var bone = finger.Bones[i];
                if (bone == null) break;
                Vector3 outgoing = bone.TransformDirection(finger.Axes[i]).normalized;
                Vector3 from = Vector3.ProjectOnPlane(incoming, hinge);
                if (outgoing.sqrMagnitude < .5f || from.sqrMagnitude < 1e-6f) { incoming = outgoing; continue; }
                from.Normalize();
                // The bend about the finger's hinge (toward the palm) and the swing out of its plane.
                Vector3 inPlane = Vector3.ProjectOnPlane(outgoing, hinge);
                float bend = Vector3.SignedAngle(from, inPlane, hinge);
                float side = Mathf.Asin(Mathf.Clamp(Vector3.Dot(outgoing, hinge), -1f, 1f)) * Mathf.Rad2Deg;
                float sideMost = i == 0 ? KnuckleSide : JointSide;
                float keptBend = Mathf.Clamp(bend, HumanBend[i].x, HumanBend[i].y);
                float keptSide = Mathf.Clamp(side, -sideMost, sideMost);
                if (Mathf.Abs(bend - keptBend) > .5f || Mathf.Abs(side - keptSide) > .5f)
                {
                    Vector3 wanted = Quaternion.AngleAxis(keptBend, hinge) * from;
                    Vector3 tilt = Vector3.Cross(wanted, hinge);
                    if (tilt.sqrMagnitude > 1e-6f) wanted = Quaternion.AngleAxis(keptSide, tilt.normalized) * wanted;
                    bone.rotation = Quaternion.FromToRotation(outgoing, wanted) * bone.rotation;
                    outgoing = wanted;
                }
                incoming = outgoing;
            }
        }
    }

    // One joint against its rest turn: the turn split into its twist along the bone's own length and the swing that is
    // left, the swing kept within mostSwing and the twist within MostTwist.
    private static void KeepFromRest(Finger finger, int joint, float mostSwing)
    {
        var bone = finger.Bones[joint];
        Vector3 along = finger.Axes[joint];
        if (bone == null || along.sqrMagnitude < .5f) return;
        along.Normalize();
        Quaternion turn = Quaternion.Inverse(finger.Rest[joint]) * bone.localRotation;
        if (turn.w < 0f) turn = new Quaternion(-turn.x, -turn.y, -turn.z, -turn.w);
        Vector3 spin = Vector3.Project(new Vector3(turn.x, turn.y, turn.z), along);
        var twist = new Quaternion(spin.x, spin.y, spin.z, turn.w);
        float size = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
        twist = size > 1e-6f ? new Quaternion(twist.x / size, twist.y / size, twist.z / size, twist.w / size) : Quaternion.identity;
        Quaternion swing = turn * Quaternion.Inverse(twist);
        float twistAngle = Signed(twist, along), swingAngle = Quaternion.Angle(Quaternion.identity, swing);
        float keptTwist = Mathf.Clamp(twistAngle, -MostTwist, MostTwist);
        if (Mathf.Abs(keptTwist - twistAngle) < .5f && swingAngle <= mostSwing + .5f) return;
        Quaternion keptSwing = swingAngle > mostSwing ? Quaternion.Slerp(Quaternion.identity, swing, mostSwing / swingAngle) : swing;
        bone.localRotation = finger.Rest[joint] * keptSwing * Quaternion.AngleAxis(keptTwist, along);
    }

    // A twist's angle about the axis it turns round, signed by that axis (-180..180).
    private static float Signed(Quaternion twist, Vector3 axis)
    {
        twist.ToAngleAxis(out float angle, out Vector3 about);
        if (angle > 180f) angle -= 360f;
        return Vector3.Dot(about, axis) < 0f ? -angle : angle;
    }

    /// <summary>The way the fingers point from the wrist, as measured on this hand.</summary>
    public Vector3 FingersForward => hand.TransformDirection(forwardInHand);

    /// <summary>
    /// The hand shapes a gun is held with: ours, the same on any humanoid. Each finger is straightened from the open hand
    /// (Rest) along the way it points, then its knuckle, middle and tip joints bend by set angles about the hand's own hinge
    /// (across the way it points and the way the palm faces), all well inside a hand's reach (KeepHuman's), the thumb by its
    /// own. KINEMATION's finger curves were made for other skeletons and ran past a hand's range (humanoid muscles to ±5),
    /// and closing the fingers onto the gun's mesh left them straight wherever a hand began inside the gun; the fingers
    /// broke one way after another (user, 2026-11-09: "우리 식으로"). A shape is worked out once per hand and kept: the
    /// angles are the hand's own, so where the hand is doesn't change them.
    /// </summary>
    public enum Shape
    {
        /// <summary>The shooting hand on a pistol grip, the index finger on the trigger.</summary>
        Trigger,
        /// <summary>The support hand under a handguard or a pump.</summary>
        Cradle,
        /// <summary>The support hand round a vertical grip.</summary>
        Fist,
        /// <summary>The support hand against an angled grip.</summary>
        Angled,
        /// <summary>The support hand under a pistol's grip hand.</summary>
        Cup,
        /// <summary>A hand off its grip mid-reload, on a magazine or a bolt.</summary>
        Grab
    }

    // Degrees for each shape (in Shape's order): the index, middle, ring and little fingers, then the thumb; each its
    // knuckle, middle joint and tip, bending toward the palm.
    private static readonly Vector3[][] ShapeAngles =
    {
        new[] { new Vector3(30f, 50f, 25f), new Vector3(75f, 95f, 55f), new Vector3(78f, 95f, 55f), new Vector3(80f, 92f, 55f), new Vector3(25f, 30f, 20f) },
        new[] { new Vector3(45f, 55f, 35f), new Vector3(50f, 60f, 40f), new Vector3(52f, 62f, 40f), new Vector3(55f, 62f, 40f), new Vector3(15f, 20f, 15f) },
        new[] { new Vector3(72f, 92f, 55f), new Vector3(75f, 95f, 55f), new Vector3(78f, 95f, 55f), new Vector3(80f, 92f, 55f), new Vector3(30f, 35f, 25f) },
        new[] { new Vector3(30f, 40f, 25f), new Vector3(30f, 40f, 25f), new Vector3(32f, 42f, 25f), new Vector3(35f, 42f, 25f), new Vector3(10f, 15f, 10f) },
        new[] { new Vector3(40f, 45f, 30f), new Vector3(45f, 50f, 30f), new Vector3(48f, 52f, 30f), new Vector3(50f, 52f, 30f), new Vector3(10f, 10f, 5f) },
        new[] { new Vector3(35f, 50f, 30f), new Vector3(40f, 55f, 35f), new Vector3(42f, 58f, 35f), new Vector3(45f, 58f, 35f), new Vector3(20f, 25f, 20f) },
    };

    private readonly Quaternion[][] shapePoses = new Quaternion[ShapeAngles.Length][];
    // A gun's own shape (GunGrips), worked out again whenever the tuner changes it.
    private Quaternion[] customPose;
    private GunGrips.Grip customFor;
    private int customVersion;

    /// <summary>A shared shape's angles (degrees; index, middle, ring, little, thumb; base, middle, tip): the Grip Tuner starts from them.</summary>
    public static Vector3[] AnglesOf(Shape shape) => (Vector3[])ShapeAngles[(int)shape].Clone();

    /// <summary>
    /// Wears shape <paramref name="on"/>, going over to <paramref name="off"/> as <paramref name="hold"/> falls from 1 to 0,
    /// <paramref name="weight"/> of the way from what the fingers have now.
    /// </summary>
    public void Wear(Shape on, Shape off, float hold, float weight)
    {
        if (weight <= 0f) return;
        Blend(PoseOf(on), PoseOf(off), hold, weight);
    }

    /// <summary>As Wear, with a gun's own shape (GunGrips: the Grip Tuner's angles) held instead of a shared one.</summary>
    public void WearCustom(GunGrips.Grip on, Shape off, float hold, float weight)
    {
        if (weight <= 0f || on == null) return;
        if (customPose == null || customFor != on || customVersion != on.Version)
        {
            customPose = SolveShape(on.Bends, on.ThumbUnder);
            customFor = on;
            customVersion = on.Version;
        }
        Blend(customPose, PoseOf(off), hold, weight);
    }
}

