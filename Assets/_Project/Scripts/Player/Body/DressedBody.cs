using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The body's real outside with its clothes on, measured from its meshes as it is made: how big each round shape that
/// cloth rests against (PlayerBodyClothColliders) really is on this body. The shapes made from the bones alone guess from
/// the shoulders and hips, and anime proportions beat them: on Mayo the head's ball was 59 mm round a head of 87, the
/// chest 53 for 83, the thighs 47 for 69, so hair and tails sank into her and into what she wore (the user, 2026-11-12:
/// "머리 충돌 공도 실제 머리 크기로 맞춰 주고 꼬리랑 머리카락에 물리 좀 넣어 줘. 몸이랑 옷이랑 충돌 안 하게끔").
/// Each part takes the points of the skin and clothes its bones carry; the cloth that hangs free (it moves) and whatever
/// isn't shown don't count. In every direction round the part's middle its outside is where nine points in ten lie
/// nearer, past the odd stray vertex and the mouth and eyes inside a head; the middle moves to the middle of that
/// outside, and the part's size is the middle size of its outside all round. A ball is round where a body isn't, so it
/// stands out of the body here and there: a cloth part lying inside it as made gets it made smaller for itself
/// (PlayerBodyClothColliders.Fit). Kept per look in the bones' own space, so the menu's preview, made again on every
/// try, measures each look once. Numbers: Tables/ClothBody (bodySurface, bodySize).
/// A soft lump on the chest (a bust, a spring of Cloth.csv) is measured as a ball of its own from the points its bones
/// carry, and the chest's rod without them: a rod round the ribs can't also reach the front of a bust, and Mayo's ran
/// some 4 cm inside hers, as far as her side locks sank into it. The ball stays on the chest, where the bust rests:
/// Magica puts a spring's bones back where they rest before it reads any collider, so a ball on them wouldn't follow the
/// jiggle either, and an object under them would become part of the spring.
/// </summary>
internal sealed class DressedBody
{
    internal enum Part { Head, Chest, Pelvis, LeftThigh, RightThigh, LeftShin, RightShin }
    private const int PartCount = 7;

    // A part with fewer points than this isn't really there: a rig without that bone's skin, or meshes that can't be read.
    private const int LeastPoints = 24;
    // Directions round a ball, as bands from one pole to the other times steps round; and steps round a rod's middle line.
    private const int Bands = 8, Round = 16;
    // At least this share of those directions must meet the part, or it is too open to measure (a stump, a lone strap).
    private const float LeastCover = .4f;
    // How many times a part's middle moves to the middle of the outside measured round it.
    private const int CentrePasses = 2;
    // Looks remembered for bodies made again in the same clothes.
    private const int Remembered = 24;
    // Along the chest's rod (chest bone to neck): where the chest's own points count, for its lower end. The neck's
    // points make its upper end.
    private const float ChestFrom = -.25f, ChestTo = .75f;

    private struct Shape
    {
        public bool Found;
        // In the part's bone's own space; a ball's middle is Start.
        public Vector3 Start, End;
        // In the bone's own units.
        public float StartRadius, EndRadius;
    }

    // A soft lump of the chest: its ball, in the space of the body bone it hangs from.
    private struct ChestLump
    {
        public HumanBodyBones Bone;
        public Shape Shape;
    }

    // One look's measure: the parts in Part's order, and the lumps.
    private sealed class Measures
    {
        public Shape[] Shapes;
        public ChestLump[] Lumps;
    }

    private static readonly Dictionary<string, Measures> remembered = new Dictionary<string, Measures>();
    private static readonly Queue<string> rememberedOrder = new Queue<string>();

    private readonly Animator animator;
    private readonly Shape[] shapes;
    private readonly ChestLump[] lumps;

    private DressedBody(Animator animator, Measures measures)
    {
        this.animator = animator;
        shapes = measures.Shapes;
        lumps = measures.Lumps;
    }

    /// <summary>How many soft lumps of the chest were measured (Lump).</summary>
    internal int LumpCount => lumps.Length;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        remembered.Clear();
        rememberedOrder.Clear();
    }

    /// <param name="key">What the body is and wears, to remember the measure by; null measures every time.</param>
    /// <param name="read">The body's meshes as they stand, read only when the look isn't remembered.</param>
    /// <param name="hanging">Every bone of the cloth that hangs free: what it carries doesn't count.</param>
    /// <param name="springs">The bones soft lumps hang from (Cloth.csv's springs): those on the chest are measured as lumps.</param>
    /// <param name="surface">The share of a direction's points that lie inside its outside.</param>
    /// <param name="size">Where, among a part's sizes all round, its size is taken (0.5: the middle one).</param>
    internal static DressedBody Measure(Animator animator, string key, Func<SkinSample> read, HashSet<Transform> hanging,
        HashSet<Transform> springs, float surface, float size)
    {
        if (key != null) key = key + "|" + surface + "|" + size;
        if (key == null || !remembered.TryGetValue(key, out var measures))
        {
            measures = Fit(animator, read(), hanging, springs, Mathf.Clamp01(surface), Mathf.Clamp01(size));
            if (key != null) Remember(key, measures);
        }
        return new DressedBody(animator, measures);
    }

    /// <summary>A soft lump of the chest as a ball: the body bone it rests on, its middle (world) and its size (metres).</summary>
    internal bool Lump(int index, out Transform bone, out Vector3 centre, out float radius)
    {
        var lump = lumps[index];
        bone = animator.GetBoneTransform(lump.Bone);
        centre = default;
        radius = 0f;
        if (bone == null) return false;
        centre = bone.TransformPoint(lump.Shape.Start);
        radius = lump.Shape.StartRadius * Mathf.Abs(bone.lossyScale.x);
        return true;
    }

    /// <summary>A part measured as a ball: the bone it moves with, its middle (world) and its size (metres).</summary>
    internal bool Ball(Part part, out Transform bone, out Vector3 centre, out float radius)
    {
        bone = Bone(part);
        var shape = shapes[(int)part];
        centre = default;
        radius = 0f;
        if (!shape.Found || bone == null) return false;
        centre = bone.TransformPoint(shape.Start);
        radius = shape.StartRadius * Mathf.Abs(bone.lossyScale.x);
        return true;
    }

    /// <summary>A part measured as a rod: the bone it moves with, its two ends (world) and their sizes (metres).</summary>
    internal bool Rod(Part part, out Transform bone, out Vector3 start, out Vector3 end, out float startRadius, out float endRadius)
    {
        bone = Bone(part);
        var shape = shapes[(int)part];
        start = end = default;
        startRadius = endRadius = 0f;
        if (!shape.Found || bone == null) return false;
        float scale = Mathf.Abs(bone.lossyScale.x);
        start = bone.TransformPoint(shape.Start);
        end = bone.TransformPoint(shape.End);
        startRadius = shape.StartRadius * scale;
        endRadius = shape.EndRadius * scale;
        return true;
    }

    private Transform Bone(Part part) => Bone(animator, part);

    private static Transform Bone(Animator animator, Part part) => part switch
    {
        Part.Head => animator.GetBoneTransform(HumanBodyBones.Head),
        Part.Chest => animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine),
        Part.Pelvis => animator.GetBoneTransform(HumanBodyBones.Hips),
        Part.LeftThigh => animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg),
        Part.RightThigh => animator.GetBoneTransform(HumanBodyBones.RightUpperLeg),
        Part.LeftShin => animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg),
        _ => animator.GetBoneTransform(HumanBodyBones.RightLowerLeg),
    };

    private static void Remember(string key, Measures measures)
    {
        if (remembered.ContainsKey(key)) return;
        remembered[key] = measures;
        rememberedOrder.Enqueue(key);
        while (rememberedOrder.Count > Remembered) remembered.Remove(rememberedOrder.Dequeue());
    }

    private static Measures Fit(Animator animator, SkinSample skin, HashSet<Transform> hanging, HashSet<Transform> springs,
        float surface, float size)
    {
        var shapes = new Shape[PartCount];
        var measures = new Measures { Shapes = shapes, Lumps = Array.Empty<ChestLump>() };
        if (animator == null || skin == null || skin.Points.Count == 0) return measures;
        // Each point belongs to the first body bone above the bone that carries most of it: a ribbon on the head is the
        // head's. A point a soft lump of the chest carries is that lump's instead.
        var human = new Dictionary<Transform, HumanBodyBones>();
        for (var id = HumanBodyBones.Hips; id < HumanBodyBones.LastBone; id++)
        {
            var bone = animator.GetBoneTransform(id);
            if (bone != null) human[bone] = id;
        }
        var lumpRoots = new List<Transform>();
        if (springs != null)
            foreach (var root in springs)
                if (root != null && Owner(root, human, animator.transform) is HumanBodyBones.Chest or HumanBodyBones.UpperChest)
                    lumpRoots.Add(root);
        var lumpPoints = new List<Vector3>[lumpRoots.Count];
        var lumpOf = new Dictionary<Transform, int>();
        var marks = skin.Marks(hanging);
        var owners = new Dictionary<Transform, HumanBodyBones>();
        var points = new Dictionary<HumanBodyBones, List<Vector3>>();
        foreach (var point in skin.Points)
        {
            if (!skin.Shown(point) || SkinSample.CarriedBy(point, marks)) continue;
            var main = skin.Main(point);
            if (main == null) continue;
            if (lumpRoots.Count > 0)
            {
                if (!lumpOf.TryGetValue(main, out int lump)) lumpOf[main] = lump = LumpOf(main, lumpRoots, human, animator.transform);
                if (lump >= 0)
                {
                    (lumpPoints[lump] ??= new List<Vector3>()).Add(point.Position);
                    continue;
                }
            }
            if (!owners.TryGetValue(main, out var owner)) owners[main] = owner = Owner(main, human, animator.transform);
            if (owner == HumanBodyBones.LastBone) continue;
            if (!points.TryGetValue(owner, out var list)) points[owner] = list = new List<Vector3>();
            list.Add(point.Position);
        }
        var lumps = new List<ChestLump>();
        for (int i = 0; i < lumpRoots.Count; i++)
        {
            if (lumpPoints[i] == null) continue;
            var owner = Owner(lumpRoots[i], human, animator.transform);
            var lump = new ChestLump { Bone = owner };
            Ball(ref lump.Shape, animator.GetBoneTransform(owner), lumpPoints[i], surface, size);
            if (lump.Shape.Found) lumps.Add(lump);
        }
        measures.Lumps = lumps.ToArray();

        Ball(ref shapes[(int)Part.Head], Bone(animator, Part.Head),
            Gather(points, HumanBodyBones.Head, HumanBodyBones.Jaw, HumanBodyBones.LeftEye, HumanBodyBones.RightEye), surface, size);
        Ball(ref shapes[(int)Part.Pelvis], Bone(animator, Part.Pelvis), Gather(points, HumanBodyBones.Hips), surface, size);
        Rod(ref shapes[(int)Part.Chest], Bone(animator, Part.Chest), animator.GetBoneTransform(HumanBodyBones.Neck),
            Gather(points, HumanBodyBones.Chest, HumanBodyBones.UpperChest), ChestFrom, ChestTo,
            Gather(points, HumanBodyBones.Neck), float.NegativeInfinity, float.PositiveInfinity, surface, size);
        Limb(ref shapes[(int)Part.LeftThigh], Bone(animator, Part.LeftThigh), animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg),
            Gather(points, HumanBodyBones.LeftUpperLeg), surface, size);
        Limb(ref shapes[(int)Part.RightThigh], Bone(animator, Part.RightThigh), animator.GetBoneTransform(HumanBodyBones.RightLowerLeg),
            Gather(points, HumanBodyBones.RightUpperLeg), surface, size);
        Limb(ref shapes[(int)Part.LeftShin], Bone(animator, Part.LeftShin), animator.GetBoneTransform(HumanBodyBones.LeftFoot),
            Gather(points, HumanBodyBones.LeftLowerLeg), surface, size);
        Limb(ref shapes[(int)Part.RightShin], Bone(animator, Part.RightShin), animator.GetBoneTransform(HumanBodyBones.RightFoot),
            Gather(points, HumanBodyBones.RightLowerLeg), surface, size);
        return measures;
    }

    private static HumanBodyBones Owner(Transform bone, Dictionary<Transform, HumanBodyBones> human, Transform top)
    {
        for (var at = bone; at != null && at != top; at = at.parent)
            if (human.TryGetValue(at, out var id)) return id;
        return HumanBodyBones.LastBone;
    }

    // Which lump a bone is part of (its own root or one above it, short of a body bone), or -1.
    private static int LumpOf(Transform bone, List<Transform> roots, Dictionary<Transform, HumanBodyBones> human, Transform top)
    {
        for (var at = bone; at != null && at != top; at = at.parent)
        {
            int index = roots.IndexOf(at);
            if (index >= 0) return index;
            if (human.ContainsKey(at)) return -1;
        }
        return -1;
    }

    private static List<Vector3> Gather(Dictionary<HumanBodyBones, List<Vector3>> points, params HumanBodyBones[] owners)
    {
        var all = new List<Vector3>();
        foreach (var owner in owners)
            if (points.TryGetValue(owner, out var list)) all.AddRange(list);
        return all;
    }

    private static void Ball(ref Shape shape, Transform bone, List<Vector3> world, float surface, float size)
    {
        if (bone == null || world.Count < LeastPoints) return;
        var local = ToLocal(bone, world);
        if (!FitBall(local, surface, size, out var centre, out float radius)) return;
        shape = new Shape { Found = true, Start = centre, End = centre, StartRadius = radius, EndRadius = radius };
    }

    // A thigh or shin: its own points, the half nearer each joint making that end.
    private static void Limb(ref Shape shape, Transform from, Transform to, List<Vector3> world, float surface, float size) =>
        Rod(ref shape, from, to, world, 0f, .5f, world, .5f, 1f, surface, size);

    private static void Rod(ref Shape shape, Transform from, Transform to, List<Vector3> startWorld, float startFrom, float startTo,
        List<Vector3> endWorld, float endFrom, float endTo, float surface, float size)
    {
        if (from == null || to == null) return;
        Vector3 axis = from.InverseTransformPoint(to.position);
        if (axis.sqrMagnitude < 1e-8f) return;
        if (!FitEnd(ToLocal(from, startWorld), axis, startFrom, startTo, surface, size, out var startShift, out float startRadius)) return;
        if (!FitEnd(ToLocal(from, endWorld), axis, endFrom, endTo, surface, size, out var endShift, out float endRadius)) return;
        shape = new Shape { Found = true, Start = startShift, End = axis + endShift, StartRadius = startRadius, EndRadius = endRadius };
    }

    private static List<Vector3> ToLocal(Transform bone, List<Vector3> world)
    {
        Matrix4x4 toLocal = bone.worldToLocalMatrix;
        var local = new List<Vector3>(world.Count);
        foreach (var point in world) local.Add(toLocal.MultiplyPoint3x4(point));
        return local;
    }

    // A ball: its middle starts at the middle of its points' box, then moves to the middle of the outside found round it.
    private static bool FitBall(List<Vector3> points, float surface, float size, out Vector3 centre, out float radius)
    {
        centre = Vector3.zero;
        radius = 0f;
        if (points.Count < LeastPoints) return false;
        var box = new Bounds(points[0], Vector3.zero);
        foreach (var point in points) box.Encapsulate(point);
        centre = box.center;
        var cells = new List<(float Distance, Vector3 Point)>[Bands * Round];
        var outer = new List<(float Distance, Vector3 Point)>(cells.Length);
        for (int pass = 0; ; pass++)
        {
            foreach (var cell in cells) cell?.Clear();
            foreach (var point in points)
            {
                Vector3 away = point - centre;
                float distance = away.magnitude;
                if (distance < 1e-6f) continue;
                int band = Mathf.Clamp((int)((Mathf.Asin(Mathf.Clamp(away.y / distance, -1f, 1f)) / Mathf.PI + .5f) * Bands), 0, Bands - 1);
                int step = Mathf.Clamp((int)((Mathf.Atan2(away.x, away.z) / (2f * Mathf.PI) + .5f) * Round), 0, Round - 1);
                (cells[band * Round + step] ??= new List<(float, Vector3)>()).Add((distance, point));
            }
            Outside(cells, surface, outer);
            if (outer.Count < cells.Length * LeastCover) return false;
            if (pass == CentrePasses) break;
            Vector3 middle = Vector3.zero;
            foreach (var (_, point) in outer) middle += point;
            centre = middle / outer.Count;
        }
        radius = Share(outer, size);
        return radius > 0f;
    }

    // One end of a rod: the points along its middle line within those shares of its length, the line moved square to
    // itself to the middle of their outside.
    private static bool FitEnd(List<Vector3> points, Vector3 axis, float from, float to, float surface, float size,
        out Vector3 shift, out float radius)
    {
        shift = Vector3.zero;
        radius = 0f;
        Vector3 along = axis.normalized;
        Vector3 square = Mathf.Abs(Vector3.Dot(along, Vector3.forward)) < .7f ? Vector3.forward : Vector3.right;
        Vector3 across = Vector3.ProjectOnPlane(square, along).normalized, aside = Vector3.Cross(along, across);
        var flat = new List<Vector2>();
        foreach (var point in points)
        {
            float t = Vector3.Dot(point, axis) / axis.sqrMagnitude;
            if (t < from || t > to) continue;
            Vector3 radial = point - axis * t;
            flat.Add(new Vector2(Vector3.Dot(radial, across), Vector3.Dot(radial, aside)));
        }
        if (flat.Count < LeastPoints) return false;
        var cells = new List<(float Distance, Vector3 Point)>[Round];
        var outer = new List<(float Distance, Vector3 Point)>(Round);
        Vector2 middle = Vector2.zero;
        for (int pass = 0; ; pass++)
        {
            foreach (var cell in cells) cell?.Clear();
            foreach (var point in flat)
            {
                Vector2 away = point - middle;
                float distance = away.magnitude;
                if (distance < 1e-6f) continue;
                int step = Mathf.Clamp((int)((Mathf.Atan2(away.y, away.x) / (2f * Mathf.PI) + .5f) * Round), 0, Round - 1);
                (cells[step] ??= new List<(float, Vector3)>()).Add((distance, (Vector3)point));
            }
            Outside(cells, surface, outer);
            if (outer.Count < cells.Length * LeastCover) return false;
            if (pass == CentrePasses) break;
            Vector2 sum = Vector2.zero;
            foreach (var (_, point) in outer) sum += (Vector2)point;
            middle = sum / outer.Count;
        }
        shift = across * middle.x + aside * middle.y;
        radius = Share(outer, size);
        return radius > 0f;
    }

    // In each direction that has points, the one at that share of their distances: the part's outside there.
    private static void Outside(List<(float Distance, Vector3 Point)>[] cells, float surface, List<(float, Vector3)> outer)
    {
        outer.Clear();
        foreach (var cell in cells)
        {
            if (cell == null || cell.Count == 0) continue;
            cell.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            outer.Add(cell[Mathf.Min(cell.Count - 1, (int)(cell.Count * surface))]);
        }
    }

    private static float Share(List<(float Distance, Vector3 Point)> outer, float share)
    {
        var distances = new List<float>(outer.Count);
        foreach (var (distance, _) in outer) distances.Add(distance);
        distances.Sort();
        return distances[Mathf.Clamp((int)(distances.Count * share), 0, distances.Count - 1)];
    }
}
