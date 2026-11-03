using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The real outside of a body's hips and legs, measured from its skinned meshes as the body is made: round each bone's
/// middle line, how far out the skin lies in each direction and at each point along it. The maps ride their bones, so
/// they follow the legs as they move, and the cloth the body wears doesn't count. Magica Cloth's colliders can only be
/// round tubes, which a thigh isn't (wider toward the hip, flatter at the front), so they stay thin enough to keep the
/// skirt from puffing out at rest; a pressed skirt stops against this instead (SkirtPress).
/// </summary>
internal sealed class BodySurface
{
    private const int Angles = 16, Sections = 6;
    // A cell's skin lies where this share of its points are nearer the bone: past the odd stray vertex.
    private const float SkinShare = .9f;
    // A part with fewer points than this isn't really there, as on a rig without that bone's skin.
    private const int LeastPoints = 24;
    // A leg's map reaches this far above its joint, into the hip, and just past its far end.
    private const float LegFrom = -.3f, LegTo = 1.05f;
    // The hips' map runs from the waist to below the hip joints, within this many of its lengths either way.
    private const float HipsReach = 2f;

    private sealed class Part
    {
        public Transform Bone;
        // In the bone's space: the middle line and two directions square to it, for angles.
        public Vector3 Axis, Across, Aside;
        // The stretch of the middle line the map covers, as shares of its length, and whether it shrinks to the skin found.
        public float From, To;
        public bool FitsSkin;
        public readonly float[] Skin = new float[Angles * Sections];
        public readonly List<Vector3> Samples = new List<Vector3>();

        // How far out the skin is at this point along the line and angle round it (the bone's units).
        public float Sample(float along, float angle)
        {
            float section = Mathf.Clamp((along - From) / (To - From) * Sections - .5f, 0f, Sections - 1f);
            float round = Mathf.Repeat((angle + Mathf.PI) / (2f * Mathf.PI) * Angles - .5f, Angles);
            int s0 = (int)section, s1 = Mathf.Min(s0 + 1, Sections - 1);
            int a0 = (int)round % Angles, a1 = (a0 + 1) % Angles;
            float s = section - s0, a = round - (int)round;
            float near = Mathf.Lerp(Skin[s0 * Angles + a0], Skin[s0 * Angles + a1], a);
            float far = Mathf.Lerp(Skin[s1 * Angles + a0], Skin[s1 * Angles + a1], a);
            return Mathf.Lerp(near, far, s);
        }
    }

    private readonly List<Part> parts = new List<Part>();

    /// <summary>Whether any of the body's skin could be read.</summary>
    internal bool Measured => parts.Count > 0;

    /// <param name="cloth">The bones of everything the body wears that hangs free: their vertices aren't skin.</param>
    internal BodySurface(Animator animator, SkinSample skin, HashSet<Transform> cloth)
    {
        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        Transform leftLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg), rightLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        var made = new Dictionary<Transform, Part>();
        if (hips != null && leftLeg != null && rightLeg != null)
            Add(made, hips, (leftLeg.position + rightLeg.position) * .5f, -HipsReach, HipsReach).FitsSkin = true;
        Add(made, leftLeg, animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg), LegFrom, LegTo);
        Add(made, rightLeg, animator.GetBoneTransform(HumanBodyBones.RightLowerLeg), LegFrom, LegTo);
        Add(made, animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg), animator.GetBoneTransform(HumanBodyBones.LeftFoot), LegFrom, LegTo);
        Add(made, animator.GetBoneTransform(HumanBodyBones.RightLowerLeg), animator.GetBoneTransform(HumanBodyBones.RightFoot), LegFrom, LegTo);
        if (made.Count == 0) return;

        // Each vertex belongs to the part its main bone is under, unless another body bone comes first on the way up.
        var human = new HashSet<Transform>();
        for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
        {
            var transform = animator.GetBoneTransform(bone);
            if (transform != null) human.Add(transform);
        }
        var owners = new Dictionary<Transform, Part>();
        foreach (var point in skin.Points)
        {
            if (skin.CarriedBy(point, cloth)) continue;
            var part = Owner(skin.Main(point), made, human, animator.transform, owners);
            if (part == null) continue;
            Vector3 local = part.Bone.InverseTransformPoint(point.Position);
            float along = Vector3.Dot(local, part.Axis) / part.Axis.sqrMagnitude;
            if (along < part.From || along > part.To) continue;
            Vector3 radial = local - part.Axis * along;
            part.Samples.Add(new Vector3(along, Mathf.Atan2(Vector3.Dot(radial, part.Aside), Vector3.Dot(radial, part.Across)), radial.magnitude));
        }
        foreach (var part in made.Values)
            if (part.Samples.Count >= LeastPoints && Map(part)) parts.Add(part);
    }

    /// <summary>
    /// How far a point is outside the body's hips and legs (metres): below zero inside them, infinity where no part of
    /// the measured skin is beside it.
    /// </summary>
    internal float Distance(Vector3 point)
    {
        float least = float.PositiveInfinity;
        foreach (var part in parts)
        {
            Vector3 local = part.Bone.InverseTransformPoint(point);
            float along = Vector3.Dot(local, part.Axis) / part.Axis.sqrMagnitude;
            if (along < part.From || along > part.To) continue;
            Vector3 radial = local - part.Axis * along;
            float angle = Mathf.Atan2(Vector3.Dot(radial, part.Aside), Vector3.Dot(radial, part.Across));
            least = Mathf.Min(least, (radial.magnitude - part.Sample(along, angle)) * Mathf.Abs(part.Bone.lossyScale.x));
        }
        return least;
    }

    private static void Add(Dictionary<Transform, Part> made, Transform bone, Transform toward, float from, float to)
    {
        if (bone != null && toward != null) Add(made, bone, toward.position, from, to);
    }

    private static Part Add(Dictionary<Transform, Part> made, Transform bone, Vector3 toward, float from, float to)
    {
        Vector3 axis = bone.InverseTransformPoint(toward);
        // A bone with no length gets a map nothing is ever measured into.
        if (axis.sqrMagnitude < 1e-8f) return new Part();
        Vector3 square = Mathf.Abs(Vector3.Dot(axis.normalized, Vector3.forward)) < .7f ? Vector3.forward : Vector3.right;
        Vector3 across = Vector3.ProjectOnPlane(square, axis).normalized;
        return made[bone] = new Part
        {
            Bone = bone, Axis = axis, Across = across, Aside = Vector3.Cross(axis.normalized, across), From = from, To = to
        };
    }

    private static Part Owner(Transform bone, Dictionary<Transform, Part> made, HashSet<Transform> human, Transform top,
        Dictionary<Transform, Part> owners)
    {
        if (bone == null) return null;
        if (owners.TryGetValue(bone, out var known)) return known;
        Part found = null;
        for (var at = bone; at != null && at != top; at = at.parent)
        {
            if (made.TryGetValue(at, out found)) break;
            if (human.Contains(at)) break;
        }
        return owners[bone] = found;
    }

    // Each cell of the map takes the skin of its points; a cell no point fell in takes its neighbours' round the bone,
    // and a stretch with none at all takes the nearest stretch's.
    private static bool Map(Part part)
    {
        if (part.FitsSkin)
        {
            float lowest = float.PositiveInfinity, highest = float.NegativeInfinity;
            foreach (var sample in part.Samples) { lowest = Mathf.Min(lowest, sample.x); highest = Mathf.Max(highest, sample.x); }
            if (highest - lowest < 1e-4f) return false;
            part.From = lowest;
            part.To = highest;
        }
        var cells = new List<float>[Angles * Sections];
        foreach (var sample in part.Samples)
        {
            int section = Mathf.Clamp((int)((sample.x - part.From) / (part.To - part.From) * Sections), 0, Sections - 1);
            int angle = Mathf.Clamp((int)((sample.y + Mathf.PI) / (2f * Mathf.PI) * Angles), 0, Angles - 1);
            (cells[section * Angles + angle] ??= new List<float>()).Add(sample.z);
        }
        part.Samples.Clear();
        var filled = new bool[Sections];
        for (int s = 0; s < Sections; s++)
        {
            for (int a = 0; a < Angles; a++)
            {
                var list = cells[s * Angles + a];
                if (list == null) { part.Skin[s * Angles + a] = float.NaN; continue; }
                list.Sort();
                part.Skin[s * Angles + a] = list[Mathf.Min(list.Count - 1, (int)(list.Count * SkinShare))];
                filled[s] = true;
            }
            if (filled[s]) FillRound(part.Skin, s);
        }
        int any = Array.IndexOf(filled, true);
        if (any < 0) return false;
        for (int s = 0; s < Sections; s++)
        {
            if (filled[s]) continue;
            int nearest = any;
            for (int other = 0; other < Sections; other++)
                if (filled[other] && Mathf.Abs(other - s) < Mathf.Abs(nearest - s)) nearest = other;
            Array.Copy(part.Skin, nearest * Angles, part.Skin, s * Angles, Angles);
        }
        return true;
    }

    // Empty cells round one stretch take the straight line between the nearest filled ones either side.
    private static void FillRound(float[] skin, int section)
    {
        int start = section * Angles;
        for (int a = 0; a < Angles; a++)
        {
            if (!float.IsNaN(skin[start + a])) continue;
            int back = 1, ahead = 1;
            while (float.IsNaN(skin[start + (a - back + Angles) % Angles])) back++;
            while (float.IsNaN(skin[start + (a + ahead) % Angles])) ahead++;
            float before = skin[start + (a - back + Angles) % Angles], after = skin[start + (a + ahead) % Angles];
            skin[start + a] = Mathf.Lerp(before, after, back / (float)(back + ahead));
        }
    }
}
