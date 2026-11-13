using UnityEngine;

// HandGrip: KINEMATION's own fingers on the gun (GunArms.Digits). One part of the class; the rest is in HandGrip.cs and the files beside it.
internal sealed partial class HandGrip
{
    // The finger pose last worked out from a gun clip's fingers (fingers' order, three joints each), what it was worked out
    // for (the clip's finger ways, the gun's surface, where this hand sat on the gun then), and the joints' turns as they
    // were before working it out.
    private Quaternion[] digitPose, digitSaved;

    private readonly Vector3[] digitFor = new Vector3[GunArms.DigitCount];

    private GunSurface digitSurface;

    private Vector3 digitAt;

    private Quaternion digitTurn;

    private bool digitSteady;

    // A hand still within this of where it sat on the gun keeps the pose worked out there (m, degrees).
    private const float DigitMove = .003f, DigitTurn = 1.5f;

    // How far a finger joint opens in one try, looking for the gun's surface (degrees).
    private const float OpenStep = 3f;

    /// <summary>
    /// The fingers as KINEMATION's own hand has them on the gun (GunArms.Digits: each finger segment's way in the hand's own
    /// frame), <paramref name="weight"/> of the way from what they have now. Each segment of this hand turns, from its open
    /// rest, the least way to point where KINEMATION's does in this hand's frame, so the clip's own grip (the trigger finger,
    /// the fingers round the grip, the support hand's fingertips on the handguard) holds on any humanoid hand, whatever its
    /// bones' axes. With <paramref name="surface"/> (a gun held still in the hand: the same pose every frame), a segment that
    /// a smaller hand would sink into the gun opens about its finger's hinge until it rests on the surface, at most
    /// <paramref name="mostOpen"/> degrees, else keeps the clip's angle (a hand already in the gun can't open out of it); that
    /// pose is worked out once and kept while the hand stays where it is on the gun.
    /// </summary>
    public void WearDigits(Vector3[] digits, float weight, GunSurface surface, float clearance, float mostOpen)
    {
        if (weight <= 0f || digits == null || digits.Length < GunArms.DigitCount) return;
        if (surface != null && surface.Empty) surface = null;
        if (surface == null || !SameDigits(digits, surface)) SolveDigits(digits, surface, clearance, mostOpen);
        for (int f = 0; f < fingers.Length; f++)
            for (int joint = 0; joint < 3; joint++)
            {
                var bone = fingers[f].Bones[joint];
                if (bone != null) bone.localRotation = Quaternion.Slerp(bone.localRotation, digitPose[f * 3 + joint], weight);
            }
    }

    private bool SameDigits(Vector3[] digits, GunSurface surface)
    {
        if (digitPose == null || !digitSteady || surface != digitSurface) return false;
        for (int i = 0; i < GunArms.DigitCount; i++)
            if ((digits[i] - digitFor[i]).sqrMagnitude > 1e-8f) return false;
        Transform gun = surface.Gun;
        Quaternion toGun = Quaternion.Inverse(gun.rotation);
        return (toGun * (hand.position - gun.position) - digitAt).sqrMagnitude <= DigitMove * DigitMove
            && Quaternion.Angle(toGun * hand.rotation, digitTurn) <= DigitTurn;
    }

    private void SolveDigits(Vector3[] digits, GunSurface surface, float clearance, float mostOpen)
    {
        if (digitPose == null)
        {
            digitPose = new Quaternion[fingers.Length * 3];
            digitSaved = new Quaternion[fingers.Length * 3];
        }
        Vector3 x = FingersForward.normalized, y = Vector3.ProjectOnPlane(PalmFacing, x).normalized, z = Vector3.Cross(x, y);
        for (int f = 0; f < fingers.Length; f++)
        {
            var finger = fingers[f];
            for (int i = 0; i < 3; i++)
            {
                digitSaved[f * 3 + i] = finger.Bones[i].localRotation;
                finger.Bones[i].localRotation = finger.Rest[i];
            }
            int digit = Mathf.Clamp(finger.Digit, 0, 4);
            for (int i = 0; i < 3; i++)
            {
                Vector3 way = digits[digit * 3 + i];
                Vector3 wanted = x * way.x + y * way.y + z * way.z;
                if (wanted.sqrMagnitude < 1e-8f) continue;
                var bone = finger.Bones[i];
                bone.rotation = Quaternion.FromToRotation(bone.TransformDirection(finger.Axes[i]), wanted) * bone.rotation;
            }
            if (surface != null && !finger.Thumb) OpenOffGun(finger, surface, clearance, mostOpen, y);
            for (int i = 0; i < 3; i++)
            {
                digitPose[f * 3 + i] = finger.Bones[i].localRotation;
                finger.Bones[i].localRotation = digitSaved[f * 3 + i];
            }
        }
        for (int i = 0; i < GunArms.DigitCount; i++) digitFor[i] = digits[i];
        digitSurface = surface;
        digitSteady = surface != null;
        if (surface == null) return;
        Transform gun = surface.Gun;
        Quaternion toGun = Quaternion.Inverse(gun.rotation);
        digitAt = toGun * (hand.position - gun.position);
        digitTurn = toGun * hand.rotation;
    }

    // Each segment of a finger, base first, that would be in the gun opens about the finger's hinge (across its open line
    // and the way the palm faces, SolveShape's) until it clears the surface by its own skin radius, at most mostOpen.
    private void OpenOffGun(Finger finger, GunSurface surface, float clearance, float mostOpen, Vector3 palm)
    {
        Vector3 open = Vector3.ProjectOnPlane(hand.TransformDirection(finger.OpenInHand), palm);
        if (open.sqrMagnitude < 1e-8f) return;
        Vector3 hinge = Vector3.Cross(open.normalized, palm);
        if (hinge.sqrMagnitude < 1e-8f) return;
        hinge.Normalize();
        for (int i = 0; i < 3; i++)
        {
            if (SegmentClear(finger, i, surface, clearance)) continue;
            var bone = finger.Bones[i];
            Quaternion kept = bone.localRotation;
            Vector3 axis = bone.InverseTransformDirection(hinge);
            bool cleared = false;
            for (float back = OpenStep; back <= mostOpen + .01f; back += OpenStep)
            {
                bone.localRotation = kept * Quaternion.AngleAxis(-back, axis);
                if (SegmentClear(finger, i, surface, clearance)) { cleared = true; break; }
            }
            if (!cleared) bone.localRotation = kept;
        }
    }

    private static bool SegmentClear(Finger finger, int i, GunSurface surface, float clearance)
    {
        var bone = finger.Bones[i];
        Vector3 start = bone.position;
        return surface.Clear(start, start + bone.TransformDirection(finger.Axes[i]) * finger.Lengths[i], finger.Radii[i] + clearance);
    }

    /// <summary>
    /// The way each finger segment points now in this hand's own frame (GunArms.Digits' terms: x the way the fingers point,
    /// y the way the palm faces, z across; index, middle, ring, little, thumb; base, middle, tip). False if the hand lacks a
    /// finger. For comparing a hand with a clip's fingers (the gun hold report).
    /// </summary>
    public bool Ways(Vector3[] into)
    {
        if (into == null || into.Length < GunArms.DigitCount) return false;
        Vector3 x = FingersForward.normalized, y = Vector3.ProjectOnPlane(PalmFacing, x).normalized, z = Vector3.Cross(x, y);
        int filled = 0;
        foreach (var finger in fingers)
        {
            int digit = Mathf.Clamp(finger.Digit, 0, 4);
            for (int i = 0; i < 3; i++)
            {
                Vector3 way = finger.Bones[i].TransformDirection(finger.Axes[i]).normalized;
                into[digit * 3 + i] = new Vector3(Vector3.Dot(way, x), Vector3.Dot(way, y), Vector3.Dot(way, z));
            }
            filled++;
        }
        return filled == 5;
    }

    /// <summary>As Ways, for shape <paramref name="shape"/> (or a gun's own, <paramref name="custom"/>) as this hand wears it.</summary>
    public bool ShapeWays(Shape shape, GunGrips.Grip custom, Vector3[] into)
    {
        var pose = custom != null ? SolveShape(custom.Bends, custom.ThumbUnder) : PoseOf(shape);
        if (pose == null) return false;
        var saved = new Quaternion[fingers.Length * 3];
        for (int f = 0; f < fingers.Length; f++)
            for (int i = 0; i < 3; i++)
            {
                saved[f * 3 + i] = fingers[f].Bones[i].localRotation;
                fingers[f].Bones[i].localRotation = pose[f * 3 + i];
            }
        bool read = Ways(into);
        for (int f = 0; f < fingers.Length; f++)
            for (int i = 0; i < 3; i++) fingers[f].Bones[i].localRotation = saved[f * 3 + i];
        return read;
    }

    private void Blend(Quaternion[] held, Quaternion[] away, float hold, float weight)
    {
        if (held == null || away == null) return;
        hold = Mathf.Clamp01(hold);
        for (int f = 0; f < fingers.Length; f++)
            for (int joint = 0; joint < 3; joint++)
            {
                var bone = fingers[f].Bones[joint];
                if (bone == null) continue;
                Quaternion target = Quaternion.Slerp(away[f * 3 + joint], held[f * 3 + joint], hold);
                bone.localRotation = Quaternion.Slerp(bone.localRotation, target, weight);
            }
    }

    // The shape's joint turns for this hand, worked out once: the fingers set to it from the open hand, read, and put back.
    private Quaternion[] PoseOf(Shape shape)
    {
        int which = (int)shape;
        if (which < 0 || which >= shapePoses.Length) return null;
        if (shapePoses[which] != null) return shapePoses[which];
        return shapePoses[which] = SolveShape(ShapeAngles[which], 0f);
    }

    // The joint turns for a set of angles on this hand: each finger straightened from the open hand along the way it points,
    // then bent by its angles about the hand's own hinge; the thumb first turned thumbUnder degrees under the palm (about the
    // line of the fingers, as PrepareThumb turns it), then bent from there. Null if the hand lacks a joint.
    private Quaternion[] SolveShape(Vector3[] angles, float thumbUnder)
    {
        var pose = new Quaternion[fingers.Length * 3];
        var saved = new Quaternion[fingers.Length * 3];
        foreach (var finger in fingers)
            for (int i = 0; i < 3; i++)
                if (finger.Bones[i] == null) return null;
        for (int f = 0; f < fingers.Length; f++)
            for (int i = 0; i < 3; i++)
            {
                saved[f * 3 + i] = fingers[f].Bones[i].localRotation;
                fingers[f].Bones[i].localRotation = fingers[f].Rest[i];
            }
        Vector3 palm = PalmFacing.normalized, forward = FingersForward.normalized;
        for (int f = 0; f < fingers.Length; f++)
        {
            var finger = fingers[f];
            if (finger.Thumb && Mathf.Abs(thumbUnder) > .01f) TurnThumbUnder(thumbUnder, forward, palm);
            // As Fit opens a finger: along the way it points from the palm; a thumb keeps its own turn and opposition.
            Vector3 open = finger.Thumb ? finger.Bones[0].TransformDirection(finger.Axes[0]).normalized
                : Vector3.ProjectOnPlane(hand.TransformDirection(finger.OpenInHand), palm).normalized;
            if (open.sqrMagnitude < .5f) open = forward;
            Vector3 hinge = Vector3.Cross(open, palm).normalized;
            for (int i = 0; !finger.Thumb && i < 3; i++)
            {
                var bone = finger.Bones[i];
                bone.rotation = Quaternion.FromToRotation(bone.TransformDirection(finger.Axes[i]), open) * bone.rotation;
            }
            Vector3 bend = angles[Mathf.Clamp(finger.Digit, 0, angles.Length - 1)];
            for (int i = 0; i < 3; i++)
            {
                var bone = finger.Bones[i];
                bone.localRotation *= Quaternion.AngleAxis(bend[i], bone.InverseTransformDirection(hinge));
            }
            for (int i = 0; i < 3; i++) pose[f * 3 + i] = finger.Bones[i].localRotation;
        }
        for (int f = 0; f < fingers.Length; f++)
            for (int i = 0; i < 3; i++) fingers[f].Bones[i].localRotation = saved[f * 3 + i];
        return pose;
    }

    // Turns the thumb's base under the palm by degrees (negative: out), about the axis PrepareThumb uses: square to the
    // thumb's sideways reach and the way the palm faces. Measured from this hand, so the same number fits any hand.
    private void TurnThumbUnder(float degrees, Vector3 forward, Vector3 palm)
    {
        if (thumb == null) return;
        Transform root = thumb.Bones[0];
        Vector3 apart = Vector3.ProjectOnPlane(thumb.Bones[1].position - root.position, forward);
        Vector3 side = Vector3.ProjectOnPlane(apart, palm);
        if (side.sqrMagnitude < 1e-8f) return;
        Vector3 axis = Vector3.Cross(side, palm).normalized;
        root.rotation = Quaternion.AngleAxis(degrees, axis) * root.rotation;
    }

    private void SolvePose(Contact contact, Vector3 forward, Vector3 palm, float clearance, Quaternion[] pose)
    {
        Fit(contact, forward, palm, clearance, 1f);
        if (contact.IsLedge)
            foreach (var finger in fingers)
                if (finger.Thumb)
                {
                    if (normals == null) normals = HandSkinCorrection.For(animator);
                    normals.Prepare(finger.Bones, left, point => contact.Clear(point, point, clearance));
                }
        for (int i = 0; i < fingers.Length; i++)
            for (int joint = 0; joint < 3; joint++)
            {
                pose[i * 3 + joint] = fingers[i].Bones[joint].localRotation;
                fingers[i].Bones[joint].localRotation = fingers[i].Before[joint];
            }
    }

    private void ApplyPose(Quaternion[] pose, float weight, float curl = 1f)
    {
        for (int i = 0; i < fingers.Length; i++)
            for (int joint = 0; joint < 3; joint++)
            {
                var bone = fingers[i].Bones[joint];
                Quaternion target = Quaternion.Slerp(fingers[i].Rest[joint], pose[i * 3 + joint], curl);
                if (bone != null) bone.localRotation = Quaternion.Slerp(bone.localRotation, target, weight);
            }
    }

    // With <paramref name="only"/> (a finger's index) just that finger is solved, the fingers before it set from
    // <paramref name="solved"/> as a whole solve would have left them (SolveStep).
    private void Fit(Contact contact, Vector3 forward, Vector3 palm, float clearance, float weight, int only = -1, Quaternion[] solved = null)
    {
        if (weight <= 0f) return;
        forward.Normalize(); palm.Normalize();
        // Skin weights can cross from one finger to its neighbour. Reset the entire hand before solving any
        // finger, so a clip's animated neighbours cannot alter the solution or cause alternating poses.
        foreach (var finger in fingers)
            for (int i = 0; i < 3; i++)
            {
                if (finger.Bones[i] == null) return;
                finger.Before[i] = finger.Bones[i].localRotation;
            }
        for (int f = 0; f < fingers.Length; f++)
            for (int i = 0; i < 3; i++)
                fingers[f].Bones[i].localRotation = solved != null && only >= 0 && f < only ? solved[f * 3 + i] : fingers[f].Rest[i];
        for (int f = 0; f < fingers.Length; f++)
        {
            if (only >= 0 && f != only) continue;
            var finger = fingers[f];
            bool ledgeThumb = finger.Thumb && contact.IsLedge;
            if (ledgeThumb)
            {
                // The thumb at ease, opened back toward the model's own only as far as the real surface asks.
                // An infinite "above the ledge" plane must not turn a free thumb into a thumbs-up pose.
                for (float back = 0f; back <= 1f; back += .25f)
                {
                    for (int i = 0; i < 3; i++) finger.Bones[i].localRotation = Quaternion.Slerp(relaxedThumb[i], finger.Rest[i], back);
                    if (SkinClear()) break;
                }
                if (!SkinClear())
                {
                    var root = finger.Bones[0];
                    Quaternion relaxed = root.localRotation;
                    Vector3 direction = finger.Bones[1].position - root.position;
                    Vector3 thumbHinge = Vector3.Cross(direction.normalized, palm).normalized;
                    Vector3 thumbAxis = root.InverseTransformDirection(thumbHinge);
                    for (float lift = 2f; lift <= 12f; lift += 2f)
                    {
                        root.localRotation = relaxed * Quaternion.AngleAxis(-lift, thumbAxis);
                        if (SkinClear()) break;
                    }
                }
                for (int i = 0; i < 3; i++)
                    finger.Bones[i].localRotation = Quaternion.Slerp(finger.Before[i], finger.Bones[i].localRotation, weight);
                continue;
            }
            // A thumb has a metacarpal and its own opposition. Keep those rest-pose offsets instead of
            // lining up all three bones like a long fourth finger, which stretches the webbing visually.
            Vector3 open = finger.Thumb ? finger.Bones[0].TransformDirection(finger.Axes[0]).normalized
                : Vector3.ProjectOnPlane(hand.TransformDirection(finger.OpenInHand), palm).normalized;
            if (open.sqrMagnitude < .5f) open = forward;
            Vector3 hinge = Vector3.Cross(open, palm).normalized;
            for (int i = 0; !finger.Thumb && i < 3; i++)
            {
                var bone = finger.Bones[i];
                bone.rotation = Quaternion.FromToRotation(bone.TransformDirection(finger.Axes[i]), open) * bone.rotation;
            }
            // The thumb's opposition and a retargeted finger's open pose must also clear the skin, not just bones.
            if (!SkinClear())
            {
                var root = finger.Bones[0];
                Quaternion flat = root.localRotation;
                Vector3 axis = root.InverseTransformDirection(hinge);
                for (float lift = 2f; lift <= (finger.Thumb ? 20f : contact.FingerLift); lift += 2f)
                {
                    root.localRotation = flat * Quaternion.AngleAxis(-lift, axis);
                    if (SkinClear()) break;
                }
            }
            for (int i = 0; i < 3; i++)
            {
                var bone = finger.Bones[i];
                Quaternion straight = bone.localRotation;
                Vector3 axis = bone.InverseTransformDirection(hinge);
                float limit = finger.Thumb ? (i == 0 ? 20f : i == 1 ? 30f : 40f)
                    : i == 0 ? 85f : i == 1 ? 105f : 80f;
                float safe = 0f;
                // Stop at first contact, rather than jumping through a thin surface to another free angle.
                for (float angle = 3f; angle <= limit + 3f; angle += 3f)
                {
                    float trial = Mathf.Min(angle, limit);
                    bone.localRotation = straight * Quaternion.AngleAxis(trial, axis);
                    if (!Clear())
                    {
                        float blocked = trial;
                        for (int refine = 0; refine < 7; refine++)
                        {
                            float middle = (safe + blocked) * .5f;
                            bone.localRotation = straight * Quaternion.AngleAxis(middle, axis);
                            if (Clear()) safe = middle; else blocked = middle;
                        }
                        break;
                    }
                    safe = trial;
                    if (trial >= limit) break;
                }
                bone.localRotation = straight * Quaternion.AngleAxis(safe, axis);

                bool Clear()
                {
                    Vector3 start = bone.position, end = start + bone.TransformDirection(finger.Axes[i]) * finger.Lengths[i];
                    return contact.Clear(start, end, finger.Radii[i] + clearance) && SkinClear();
                }
            }
            for (int i = 0; i < 3; i++) finger.Bones[i].localRotation = Quaternion.Slerp(finger.Before[i], finger.Bones[i].localRotation, weight);

            bool SkinClear()
            {
                for (int i = 0; i < finger.SkinBones.Length; i++) finger.SkinMatrices[i] = finger.SkinBones[i].localToWorldMatrix;
                foreach (var vertex in finger.Skin)
                {
                    Vector3 point = vertex.Position(finger.SkinMatrices);
                    // Cover the small triangles between the sampled extremes of a phalanx's skin.
                    if (!contact.Clear(point, point, clearance + .0015f)) return false;
                }
                return true;
            }
        }
    }

    private readonly struct Contact
    {
        private readonly Collider surface;
        private readonly Vector3 centre, forward, palm;
        private readonly float half;
        private readonly bool box;
        private readonly Vector3 boxSize;
        private readonly Quaternion boxFrame;
        private readonly GunSurface gunSurface;
        public bool IsLedge => surface != null;
        // How far a straight finger may bend back to clear the surface. On a gun the palm already rests outside it, so a
        // finger bent far back is a finger thrown open off the gun (user, 2026-11-08).
        public float FingerLift => gunSurface != null ? 10f : 60f;
        public Contact(GunSurface gunSurface, Vector3 forward, Vector3 palm)
        {
            this.gunSurface = gunSurface; this.forward = forward; this.palm = palm;
            surface = null; centre = boxSize = Vector3.zero; half = 0f; boxFrame = Quaternion.identity; box = false;
        }
        public Contact(Collider surface, Vector3 forward, Vector3 palm)
        {
            this.surface = surface; this.forward = forward; this.palm = palm; gunSurface = null;
            centre = boxSize = Vector3.zero; half = 0f; boxFrame = Quaternion.identity; box = false;
            if (surface is BoxCollider bounds)
            {
                box = true;
                centre = bounds.transform.TransformPoint(bounds.center);
                Vector3 scale = bounds.transform.lossyScale;
                boxSize = Vector3.Scale(bounds.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))) * .5f;
                boxFrame = Quaternion.Inverse(bounds.transform.rotation);
            }
        }
        public Contact(Vector3 centre, Vector3 forward, Vector3 palm, float half)
        {
            surface = null; this.centre = centre; this.forward = forward; this.palm = palm; this.half = half;
            box = false; boxSize = Vector3.zero; boxFrame = Quaternion.identity; gunSurface = null;
        }

        public void Key(Transform hand, float reach, out Vector3 min, out Vector3 max, out Quaternion frame, out int shape)
        {
            if (box)
            {
                Vector3 point = boxFrame * (hand.position - centre);
                min = -boxSize - point; max = boxSize - point;
                // Distant side/end faces cannot touch this hand; sliding along a broad edge reuses its pose.
                for (int i = 0; i < 3; i++) { min[i] = Mathf.Clamp(min[i], -reach, reach); max[i] = Mathf.Clamp(max[i], -reach, reach); }
                frame = Quaternion.Inverse(hand.rotation) * Quaternion.Inverse(boxFrame);
                shape = 0;
            }
            else
            {
                min = surface.transform.InverseTransformPoint(hand.position);
                max = Vector3.zero;
                frame = Quaternion.Inverse(hand.rotation) * surface.transform.rotation;
                shape = surface.GetInstanceID();
            }
        }

        public bool Clear(Vector3 start, Vector3 end, float padding)
        {
            if (gunSurface != null) return gunSurface.Clear(start, end, padding);
            if (surface != null)
            {
                if (box) return BoxClear(boxFrame * (start - centre), boxFrame * (end - centre), boxSize, padding);
                Vector3 line = end - start;
                float length = line.magnitude;
                if (length > .0001f && surface.Raycast(new Ray(start, line / length), out _, length)) return false;
                int samples = Mathf.Clamp(Mathf.CeilToInt(length / Mathf.Max(.001f, padding * .5f)), 2, 24);
                for (int i = 0; i <= samples; i++)
                {
                    Vector3 point = Vector3.Lerp(start, end, i / (float)samples);
                    if (surface is MeshCollider mesh && !mesh.convex)
                    {
                        // ClosestPoint is unsupported on non-convex meshes. Probe the top and the front face.
                        if (NearFace(point, palm, padding) || NearFace(point, forward, padding)) return false;
                    }
                    else if ((surface.ClosestPoint(point) - point).sqrMagnitude < padding * padding) return false;
                }
                return true;
            }
            Vector2 a = Project(start), b = Project(end);
            float enter = 0f, exit = 1f;
            if (Slab(a.x, b.x - a.x, ref enter, ref exit) && Slab(a.y, b.y - a.y, ref enter, ref exit)) return false;
            float distance = Mathf.Min(PointBox(a), PointBox(b));
            distance = Mathf.Min(distance, PointSegment(new Vector2(-half, -half), a, b));
            distance = Mathf.Min(distance, PointSegment(new Vector2(-half, half), a, b));
            distance = Mathf.Min(distance, PointSegment(new Vector2(half, -half), a, b));
            distance = Mathf.Min(distance, PointSegment(new Vector2(half, half), a, b));
            return distance >= padding * padding;
        }

        private bool NearFace(Vector3 point, Vector3 inward, float padding)
            => surface.Raycast(new Ray(point - inward * .25f, inward), out RaycastHit hit, .25f + padding)
                && Vector3.Dot(hit.point - point, inward) <= padding;

        // Exact segment-to-box distance. Between crossings of the six face planes the squared distance is a
        // quadratic, so each interval has one minimum. No Physics queries or per-frame allocations for box ledges.
        private static bool BoxClear(Vector3 a, Vector3 b, Vector3 size, float padding)
        {
            Vector3 line = b - a;
            float from = 0f;
            for (int interval = 0; interval < 7; interval++)
            {
                float to = 1f;
                for (int axis = 0; axis < 3; axis++)
                {
                    if (Mathf.Abs(line[axis]) < 1e-8f) continue;
                    float low = (-size[axis] - a[axis]) / line[axis], high = (size[axis] - a[axis]) / line[axis];
                    if (low > from + 1e-7f) to = Mathf.Min(to, low);
                    if (high > from + 1e-7f) to = Mathf.Min(to, high);
                }
                Vector3 middle = a + line * ((from + to) * .5f);
                float numerator = 0f, denominator = 0f;
                for (int axis = 0; axis < 3; axis++)
                {
                    if (Mathf.Abs(middle[axis]) <= size[axis]) continue;
                    float edge = Mathf.Sign(middle[axis]) * size[axis];
                    numerator += line[axis] * (a[axis] - edge);
                    denominator += line[axis] * line[axis];
                }
                float at = denominator > 1e-12f ? Mathf.Clamp(-numerator / denominator, from, to) : from;
                Vector3 p = a + line * at;
                Vector3 delta = new Vector3(Mathf.Max(0f, Mathf.Abs(p.x) - size.x), Mathf.Max(0f, Mathf.Abs(p.y) - size.y), Mathf.Max(0f, Mathf.Abs(p.z) - size.z));
                if (delta.sqrMagnitude < padding * padding) return false;
                if (to >= 1f) break;
                from = to;
            }
            return true;
        }

        private Vector2 Project(Vector3 point) => new Vector2(Vector3.Dot(point - centre, forward), Vector3.Dot(point - centre, palm));
        private bool Slab(float start, float direction, ref float enter, ref float exit)
        {
            if (Mathf.Abs(direction) < 1e-7f) return Mathf.Abs(start) <= half;
            float a = (-half - start) / direction, b = (half - start) / direction;
            enter = Mathf.Max(enter, Mathf.Min(a, b)); exit = Mathf.Min(exit, Mathf.Max(a, b));
            return enter <= exit;
        }
        private float PointBox(Vector2 point)
        {
            Vector2 outside = new Vector2(Mathf.Max(0f, Mathf.Abs(point.x) - half), Mathf.Max(0f, Mathf.Abs(point.y) - half));
            return outside.sqrMagnitude;
        }
        private static float PointSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 line = b - a;
            return (point - a - line * Mathf.Clamp01(Vector2.Dot(point - a, line) / Mathf.Max(1e-12f, line.sqrMagnitude))).sqrMagnitude;
        }
    }
}
