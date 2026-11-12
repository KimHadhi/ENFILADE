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
/// jiggle either, and an object under them would become part of the spring. A lump is a dome lying against the ribs, not
/// a round part, so its ball is the sphere that fits its points best (Dome), which lies on the dome itself.
/// It also measures how far each hanging bone's own cloth reaches from the bone toward the body (Reach): how thick that
/// cloth is where it would touch, which a particle of the simulation needs to be, no more.
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
    // A lump's sphere is refused when it comes out larger than this many times the half-diagonal of its points' box: a
    // nearly flat chest's patch fits a sphere metres across, which would stand out round the whole body like a wall.
    private const float LargestDome = 1.5f;

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

    // One look's measure: the parts in Part's order, the lumps, and each hanging bone's cloth reach by the bone's name
    // (in its own units).
    private sealed class Measures
    {
        public Shape[] Shapes;
        public ChestLump[] Lumps;
        public Dictionary<string, float> Reach;
    }

    private static readonly Dictionary<string, Measures> remembered = new Dictionary<string, Measures>();
    private static readonly Queue<string> rememberedOrder = new Queue<string>();

    private readonly Animator animator;
    private readonly Shape[] shapes;
    private readonly ChestLump[] lumps;
    private readonly Dictionary<string, float> reach;

    private DressedBody(Animator animator, Measures measures)
    {
        this.animator = animator;
        shapes = measures.Shapes;
        lumps = measures.Lumps;
        reach = measures.Reach;
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
    /// <param name="clothShare">Where, among the reaches of a hanging bone's cloth toward the body, its reach is taken.</param>
    internal static DressedBody Measure(Animator animator, string key, Func<SkinSample> read, HashSet<Transform> hanging,
        HashSet<Transform> springs, float surface, float size, float clothShare)
    {
        if (key != null) key = key + "|" + surface + "|" + size + "|" + clothShare;
        if (key == null || !remembered.TryGetValue(key, out var measures))
        {
            measures = Fit(animator, read(), hanging, springs, Mathf.Clamp01(surface), Mathf.Clamp01(size), Mathf.Clamp01(clothShare));
            if (key != null) Remember(key, measures);
        }
        return new DressedBody(animator, measures);
    }

    /// <summary>
    /// How far the cloth a hanging bone carries reaches from the bone's line toward the body (metres): how thick a particle
    /// on that bone must be for the cloth to meet what it rests on, not stop short of it. False where the bone carries
    /// none of its cloth's vertices (the far end of a chain) or couldn't be measured.
    /// </summary>
    internal bool Reach(Transform bone, out float metres)
    {
        metres = 0f;
        if (bone == null || reach == null || !reach.TryGetValue(bone.name, out float units)) return false;
        metres = units * Mathf.Abs(bone.lossyScale.x);
        return true;
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
        float surface, float size, float clothShare)
    {
        var shapes = new Shape[PartCount];
        var measures = new Measures { Shapes = shapes, Lumps = Array.Empty<ChestLump>(), Reach = new Dictionary<string, float>() };
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
        // The hanging cloth's own vertices, by the bone that carries each most: how thick it is round that bone (Reach).
        var cloth = new Dictionary<Transform, List<Vector3>>();
        foreach (var point in skin.Points)
        {
            if (!skin.Shown(point)) continue;
            if (SkinSample.CarriedBy(point, marks))
            {
                var carrier = skin.Main(point);
                if (carrier != null && hanging.Contains(carrier))
                {
                    if (!cloth.TryGetValue(carrier, out var own)) cloth[carrier] = own = new List<Vector3>();
                    own.Add(point.Position);
                }
                continue;
            }
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
            Dome(ref lump.Shape, animator.GetBoneTransform(owner), lumpPoints[i], surface, size);
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
        ClothReach(animator, cloth, hanging, measures, clothShare);
        return measures;
    }

    // How far each hanging bone's cloth reaches from the bone toward the body: the vertices that bone carries most, each
    // measured square to the bone's two segments (to its parent and to its first hanging child), along the way from the
    // bone to the nearest measured part; the share-th of those, so a stray vertex doesn't decide it. Where its cloth goes
    // farther from the body than toward it (a strand over a coat), the reach is none: the particle can be as thin as can.
    private static void ClothReach(Animator animator, Dictionary<Transform, List<Vector3>> cloth, HashSet<Transform> hanging,
        Measures measures, float share)
    {
        if (cloth.Count == 0) return;
        var body = new DressedBody(animator, measures);
        var cores = new List<(Vector3 A, Vector3 B, float RadiusA, float RadiusB)>();
        for (int i = 0; i < PartCount; i++)
        {
            var part = (Part)i;
            if (part is Part.Head or Part.Pelvis)
            {
                if (body.Ball(part, out _, out var centre, out float radius)) cores.Add((centre, centre, radius, radius));
            }
            else if (body.Rod(part, out _, out var a, out var b, out float radiusA, out float radiusB)) cores.Add((a, b, radiusA, radiusB));
        }
        for (int i = 0; i < body.LumpCount; i++)
            if (body.Lump(i, out _, out var centre, out float radius)) cores.Add((centre, centre, radius, radius));
        if (cores.Count == 0) return;
        var along = new List<float>();
        foreach (var pair in cloth)
        {
            var bone = pair.Key;
            Vector3 at = bone.position, toward = Vector3.zero;
            float nearest = float.PositiveInfinity;
            foreach (var (a, b, radiusA, radiusB) in cores)
            {
                Vector3 line = b - a;
                float t = line.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector3.Dot(at - a, line) / line.sqrMagnitude) : 0f;
                Vector3 core = a + line * t;
                float gap = Vector3.Distance(at, core) - Mathf.Lerp(radiusA, radiusB, t);
                if (gap >= nearest) continue;
                nearest = gap;
                toward = core - at;
            }
            if (toward.sqrMagnitude < 1e-12f) continue;
            toward.Normalize();
            Vector3 up = bone.parent != null ? bone.parent.position - at : Vector3.zero, down = Vector3.zero;
            foreach (Transform child in bone)
                if (hanging.Contains(child))
                {
                    down = child.position - at;
                    break;
                }
            along.Clear();
            foreach (var point in pair.Value)
            {
                Vector3 offset = point - at;
                Vector3 onUp = OnSegment(offset, up), onDown = OnSegment(offset, down);
                offset -= (offset - onUp).sqrMagnitude <= (offset - onDown).sqrMagnitude ? onUp : onDown;
                along.Add(Vector3.Dot(offset, toward));
            }
            along.Sort();
            float extent = Mathf.Max(0f, along[Mathf.Clamp((int)(along.Count * share), 0, along.Count - 1)]);
            measures.Reach[bone.name] = extent / Mathf.Max(Mathf.Abs(bone.lossyScale.x), 1e-6f);
        }
    }

    // The point of the segment from the origin to end nearest a point.
    private static Vector3 OnSegment(Vector3 point, Vector3 end) =>
        end.sqrMagnitude > 1e-12f ? end * Mathf.Clamp01(Vector3.Dot(point, end) / end.sqrMagnitude) : Vector3.zero;

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

    // A soft lump of the chest: a dome of skin, with whatever is worn on it, lying against the ribs. The sphere that fits
    // its points best by least squares lies on the dome, its middle behind it. Ball's measure, centred on the middle of
    // the dome's outside and sized to reach its rim, stood out of Mayo's bust by 14 mm at the front and 9 at the top and
    // the outer side (with her White Board jacket on or bare), and held hair and coats that far off her chest when they
    // swung onto it; the fitted sphere's surface lies on her real one, its middle within a millimetre of it at the front,
    // top, outer side and below, dressed or bare, and Ichigo's too (the user, 2026-11-12: "가슴에 비해 좀 충돌 판정이 여유가
    // 꽤 돼서 머리랑 겉옷이 허공에 떠 있는 느낌").
    // What isn't round enough to be measured as Ball does (a flat strap between Ichigo's breasts) stays no lump, as
    // before; one too flat for its sphere (LargestDome) keeps Ball's measure.
    private static void Dome(ref Shape shape, Transform bone, List<Vector3> world, float surface, float size)
    {
        if (bone == null || world.Count < LeastPoints) return;
        var local = ToLocal(bone, world);
        if (!FitBall(local, surface, size, out var centre, out float radius)) return;
        if (FitSphere(local, out var fitted, out float fittedRadius)) (centre, radius) = (fitted, fittedRadius);
        shape = new Shape { Found = true, Start = centre, End = centre, StartRadius = radius, EndRadius = radius };
    }

    // The least-squares sphere through these points, |p|² = 2 c·p + k with r² = k + |c|², solved round their mean for
    // precision; refused when it comes out larger than LargestDome times their spread.
    private static bool FitSphere(List<Vector3> points, out Vector3 centre, out float radius)
    {
        centre = Vector3.zero;
        radius = 0f;
        if (points.Count < LeastPoints) return false;
        Vector3 mean = Vector3.zero;
        foreach (var point in points) mean += point;
        mean /= points.Count;
        var box = new Bounds(points[0] - mean, Vector3.zero);
        var normal = new double[4, 5];
        var row = new double[4];
        row[3] = 1.0;
        foreach (var point in points)
        {
            Vector3 p = point - mean;
            box.Encapsulate(p);
            row[0] = 2.0 * p.x;
            row[1] = 2.0 * p.y;
            row[2] = 2.0 * p.z;
            double w = (double)p.x * p.x + (double)p.y * p.y + (double)p.z * p.z;
            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 4; j++) normal[i, j] += row[i] * row[j];
                normal[i, 4] += row[i] * w;
            }
        }
        // Gauss-Jordan with the largest pivot in each column.
        for (int column = 0; column < 4; column++)
        {
            int pivot = column;
            for (int r = column + 1; r < 4; r++)
                if (Math.Abs(normal[r, column]) > Math.Abs(normal[pivot, column])) pivot = r;
            if (Math.Abs(normal[pivot, column]) < 1e-18) return false;
            for (int k = 0; k < 5; k++) (normal[column, k], normal[pivot, k]) = (normal[pivot, k], normal[column, k]);
            for (int r = 0; r < 4; r++)
            {
                if (r == column) continue;
                double factor = normal[r, column] / normal[column, column];
                for (int k = column; k < 5; k++) normal[r, k] -= factor * normal[column, k];
            }
        }
        double cx = normal[0, 4] / normal[0, 0], cy = normal[1, 4] / normal[1, 1], cz = normal[2, 4] / normal[2, 2];
        double squared = normal[3, 4] / normal[3, 3] + cx * cx + cy * cy + cz * cz;
        if (!(squared > 0.0) || double.IsInfinity(squared)) return false;
        radius = (float)Math.Sqrt(squared);
        if (radius > box.extents.magnitude * LargestDome) return false;
        centre = mean + new Vector3((float)cx, (float)cy, (float)cz);
        return true;
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
