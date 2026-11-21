using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The heels a look stands in, measured from what it draws, so the feet can sound like them (the user, 2026-11-21: "옷장에서
/// 발이 하이힐이면 하이힐 사운드로 해줘. 근데 기본이 하이힐인 애들도 있을 거 아니야 … 난 모드로 캐릭터를 추가할 거라서 알아서").
/// Nothing is written down per shoe or per character: a character's own shoes, a shoe from the wardrobe and a modder's
/// avatar are all read the same way, from the meshes on the feet as the model stands before any clip (DrawnMesh).
/// What a heel is, seen from the floor: under a heeled shoe the heel's tip at the back and the ball at the front both
/// touch the ground and the sole between them rises, the heel's height at the heel's breast. So each foot's underside is
/// laid out along where the foot points (the lowest drawn point at each step along it, triangle edges included, so a
/// sole of a few large faces is read as well as a dense one) and three things are read off it: how far the back reaches
/// down, how far the front does, and how high the sole stands between the two. A heel is when both ends reach the floor
/// and the middle stands clear of it.
/// - Bare feet shaped for heels (the key BOOTH avatars carry for their own pumps) stand on their toes with the heel up:
///   the back doesn't reach down, so they are no heels. Stockings or socks on such a foot the same.
/// - Flat shoes and bare feet touch along their length: their middle stands a few millimetres up at most.
/// - A wedge, its sole filled in down to the ground, reads as flat.
/// What is on a foot is what is skinned to its foot bone or a bone under it (shoes merged onto the body's feet go under
/// them), or hung there unskinned; a skirt's hem or a long coat over the shoes is not.
/// </summary>
internal static class DrawnHeels
{
    /// <summary>
    /// What feet in heels stand in, as the sounds name it: a cue's version for them is "StepWalk@Heels" in SoundCues.csv
    /// (CharacterSounds.Footwear, PlayerBody.Footwear).
    /// </summary>
    internal const string Footwear = "Heels";

    /// <summary>What one foot's underside says, in the model's units.</summary>
    internal readonly struct Foot
    {
        /// <summary>The foot had enough drawn on it to be read.</summary>
        public readonly bool Read;
        /// <summary>How high the sole stands between the heel's tip and the ball: the heel. Zero unless both ends touch.</summary>
        public readonly float Heel;
        /// <summary>How far over the foot's lowest point the back and the front reach down (the heel's tip, the ball).</summary>
        public readonly float Back, Front;
        /// <summary>The highest the underside stands between the back's lowest point and the ball, touching or not.</summary>
        public readonly float Middle;
        /// <summary>The foot's length along where it points, and how many drawn points it was read from.</summary>
        public readonly float Length;
        public readonly int Points;

        public Foot(float heel, float back, float front, float middle, float length, int points)
        {
            Read = true;
            Heel = heel; Back = back; Front = front; Middle = middle; Length = length; Points = points;
        }
    }

    // The underside is read in this many steps along the foot. The back is the last 30% of its length (a heel's tip
    // stands under the foot's own heel, at most this far forward), the front the first 40% (the ball and the toes), and
    // the sole is looked at for its middle from the back's lowest point to where the front starts.
    private const int Steps = 40, BackSteps = Steps * 3 / 10, FrontStart = Steps * 6 / 10;
    // Both ends touch when they reach down to within this share of how high the middle stands: a heel's tip and the ball
    // are modelled level within a few millimetres; a foot on its toes holds its heel as high as the arch or higher.
    private const float Touching = .35f;
    // A foot drawn from fewer points than this can't be read (a stub, a single quad).
    private const int FewestPoints = 24;

    // Reused between measures.
    private static readonly List<Vector3> points = new List<Vector3>();
    private static readonly List<int> triangles = new List<int>();
    private static readonly List<Vector2>[] laid = { new List<Vector2>(), new List<Vector2>() };
    private static readonly List<Vector4>[] edges = { new List<Vector4>(), new List<Vector4>() };
    private static readonly float[] lowest = new float[Steps];
    private static int[] sides = Array.Empty<int>();
    private static Vector2[] along = Array.Empty<Vector2>();

    /// <summary>
    /// How high the heels <paramref name="model"/> draws are, in its own units: the lower of its two feet's (a heel needs
    /// both), or the one foot's that could be read; zero for flat shoes, bare feet, or feet that can't be read. Measure it
    /// before any clip has posed the model, as DrawnSole. Renderers in <paramref name="ignored"/> don't count.
    /// </summary>
    internal static float Of(Transform model, Animator animator, ICollection<Renderer> ignored = null)
    {
        var (left, right) = Measure(model, animator, ignored);
        return Of(left, right);
    }

    /// <summary>The heels two read feet stand on: the lower of the two, or the one foot's that could be read.</summary>
    internal static float Of(Foot left, Foot right)
    {
        if (left.Read && right.Read) return Mathf.Min(left.Heel, right.Heel);
        return left.Read ? left.Heel : right.Read ? right.Heel : 0f;
    }

    /// <summary>
    /// What feet on heels <paramref name="height"/> high stand in, as the sounds name it: <see cref="Footwear"/> from
    /// <paramref name="from"/> up (Player.csv Body.heelsFrom; both in the same units), else null for ordinary shoes.
    /// </summary>
    internal static string FootwearOf(float height, float from) => height > 0f && height >= from ? Footwear : null;

    /// <summary>Both feet as <see cref="Of"/> reads them, for the wardrobe's bench.</summary>
    internal static (Foot Left, Foot Right) Measure(Transform model, Animator animator, ICollection<Renderer> ignored = null)
    {
        if (model == null || animator == null || !animator.isHuman) return default;
        var leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        var rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (leftFoot == null || rightFoot == null) return default;
        Matrix4x4 toModel = model.worldToLocalMatrix;
        var feet = new[]
        {
            Way(toModel, leftFoot, animator.GetBoneTransform(HumanBodyBones.LeftToes)),
            Way(toModel, rightFoot, animator.GetBoneTransform(HumanBodyBones.RightToes))
        };
        for (int k = 0; k < 2; k++)
        {
            laid[k].Clear();
            edges[k].Clear();
        }
        foreach (var renderer in model.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || (ignored != null && ignored.Contains(renderer))) continue;
            if (!(renderer is SkinnedMeshRenderer || renderer is MeshRenderer)) continue;
            var mesh = DrawnMesh.MeshOf(renderer);
            if (mesh == null || !mesh.isReadable || !Sides(renderer, mesh, leftFoot, rightFoot)) continue;
            DrawnMesh.Read(renderer, mesh, toModel, points);
            Lay(mesh, feet);
        }
        return (Underside(0), Underside(1));
    }

    // Where a foot stands and points, in the model's frame: from its ankle level towards its toes, or the way a humanoid
    // model faces (+z) when it has no toes or they stand straight under the ankle.
    private static (Vector3 Ankle, Vector3 Ahead) Way(Matrix4x4 toModel, Transform foot, Transform toes)
    {
        Vector3 ankle = toModel.MultiplyPoint3x4(foot.position), ahead = Vector3.forward;
        if (toes != null)
        {
            Vector3 toward = toModel.MultiplyPoint3x4(toes.position) - ankle;
            toward.y = 0f;
            if (toward.sqrMagnitude > 1e-8f) ahead = toward.normalized;
        }
        return (ankle, ahead);
    }

    // Which foot each vertex of the mesh is on (1 left, 2 right, 0 neither): skinned at least half to the foot's bone or a
    // bone under it; an unskinned mesh by where it hangs. False when none is on a foot.
    private static bool Sides(Renderer renderer, Mesh mesh, Transform leftFoot, Transform rightFoot)
    {
        int count = mesh.vertexCount;
        if (sides.Length < count) sides = new int[count];
        var bones = renderer is SkinnedMeshRenderer skinned ? skinned.bones : null;
        var perVertex = bones != null && bones.Length > 0 ? mesh.GetBonesPerVertex() : default;
        if (bones == null || bones.Length == 0 || perVertex.Length != count || mesh.bindposes.Length == 0)
        {
            // Drawn where its object holds it (DrawnMesh.Read): on the foot it hangs under.
            Transform holder = renderer.transform;
            int side = holder.IsChildOf(leftFoot) ? 1 : holder.IsChildOf(rightFoot) ? 2 : 0;
            Array.Clear(sides, 0, count);
            if (side == 0) return false;
            for (int i = 0; i < count; i++) sides[i] = side;
            return true;
        }
        var boneSide = new int[bones.Length];
        bool any = false;
        for (int b = 0; b < bones.Length; b++)
        {
            var bone = bones[b];
            boneSide[b] = bone == null ? 0 : bone.IsChildOf(leftFoot) ? 1 : bone.IsChildOf(rightFoot) ? 2 : 0;
            any |= boneSide[b] != 0;
        }
        if (!any) return false;
        var weights = mesh.GetAllBoneWeights();
        int at = 0;
        any = false;
        for (int i = 0; i < count; i++)
        {
            float onLeft = 0f, onRight = 0f, total = 0f;
            for (int n = perVertex[i]; n > 0; n--, at++)
            {
                var weight = weights[at];
                if (weight.boneIndex >= bones.Length) continue;
                total += weight.weight;
                if (boneSide[weight.boneIndex] == 1) onLeft += weight.weight;
                else if (boneSide[weight.boneIndex] == 2) onRight += weight.weight;
            }
            sides[i] = total <= 0f ? 0 : onLeft >= total * .5f ? 1 : onRight >= total * .5f ? 2 : 0;
            any |= sides[i] != 0;
        }
        return any;
    }

    // The drawn points on each foot laid out along it (how far ahead of the ankle, how high), and the edges of the
    // triangles wholly on it.
    private static void Lay(Mesh mesh, (Vector3 Ankle, Vector3 Ahead)[] feet)
    {
        int count = points.Count;
        if (along.Length < count) along = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            int side = sides[i];
            Vector3 point = points[i];
            if (side == 0 || float.IsNaN(point.y))
            {
                // Read off the edges below: a point nobody draws or on no foot makes no edge.
                sides[i] = 0;
                continue;
            }
            var foot = feet[side - 1];
            along[i] = new Vector2(Vector3.Dot(point - foot.Ankle, foot.Ahead), point.y);
            laid[side - 1].Add(along[i]);
        }
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
            mesh.GetTriangles(triangles, sub);
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                int side = sides[a];
                if (side == 0 || sides[b] != side || sides[c] != side) continue;
                var list = edges[side - 1];
                list.Add(new Vector4(along[a].x, along[a].y, along[b].x, along[b].y));
                list.Add(new Vector4(along[b].x, along[b].y, along[c].x, along[c].y));
                list.Add(new Vector4(along[c].x, along[c].y, along[a].x, along[a].y));
            }
        }
    }

    // One foot's underside: the lowest drawn point at each step along it, then what its back, front and middle say.
    private static Foot Underside(int foot)
    {
        var laidOut = laid[foot];
        if (laidOut.Count < FewestPoints) return default;
        float first = float.PositiveInfinity, last = float.NegativeInfinity, floor = float.PositiveInfinity;
        foreach (var point in laidOut)
        {
            first = Mathf.Min(first, point.x);
            last = Mathf.Max(last, point.x);
            floor = Mathf.Min(floor, point.y);
        }
        float length = last - first;
        if (length <= 1e-4f) return default;
        float step = length / Steps;
        for (int s = 0; s < Steps; s++) lowest[s] = float.PositiveInfinity;
        foreach (var point in laidOut)
        {
            int s = Mathf.Clamp((int)((point.x - first) / step), 0, Steps - 1);
            lowest[s] = Mathf.Min(lowest[s], point.y);
        }
        // An edge crossing a step's middle is drawn there too: a long flat face has no points of its own in between.
        foreach (var edge in edges[foot])
        {
            float fromAt = edge.x, fromY = edge.y, toAt = edge.z, toY = edge.w;
            if (fromAt > toAt)
            {
                (fromAt, toAt) = (toAt, fromAt);
                (fromY, toY) = (toY, fromY);
            }
            int from = Mathf.Max(0, Mathf.CeilToInt((fromAt - first) / step - .5f));
            int to = Mathf.Min(Steps - 1, Mathf.FloorToInt((toAt - first) / step - .5f));
            for (int s = from; s <= to; s++)
            {
                float middle = first + (s + .5f) * step;
                float y = toAt - fromAt > 1e-7f ? Mathf.Lerp(fromY, toY, (middle - fromAt) / (toAt - fromAt)) : Mathf.Min(fromY, toY);
                lowest[s] = Mathf.Min(lowest[s], y);
            }
        }
        // Back is the start of the length (behind the ankle), front its end: Way points the length at the toes.
        float back = float.PositiveInfinity, front = float.PositiveInfinity, raised = 0f;
        int backAt = 0;
        for (int s = 0; s < BackSteps; s++)
            if (lowest[s] < back) { back = lowest[s]; backAt = s; }
        for (int s = FrontStart; s < Steps; s++) front = Mathf.Min(front, lowest[s]);
        for (int s = backAt + 1; s < FrontStart; s++)
            if (!float.IsInfinity(lowest[s])) raised = Mathf.Max(raised, lowest[s] - floor);
        back -= floor;
        front -= floor;
        bool touching = back <= raised * Touching && front <= raised * Touching;
        return new Foot(touching ? raised : 0f, back, front, raised, length, laidOut.Count);
    }
}
