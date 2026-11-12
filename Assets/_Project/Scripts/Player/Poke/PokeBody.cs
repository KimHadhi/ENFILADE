using System.Collections.Generic;
using UnityEngine;

/// <summary>Where a pointing finger's line meets a friend's body, and what part of them it is (SkinPoke.Find).</summary>
public readonly struct PokeHit
{
    /// <summary>The friend pointed at.</summary>
    public readonly PlayerMovement Target;
    internal readonly PokeBody Body;
    /// <summary>Where the line meets their outermost surface (skin, hair or clothes), in the world.</summary>
    public readonly Vector3 Point;
    /// <summary>The bone that surface rides (the point stays on it while they move).</summary>
    internal readonly Transform Bone;
    public readonly PokeZone Zone;
    /// <summary>They let friends poke them (GameSettings.letOthersPoke, carried by their pose).</summary>
    public readonly bool Allowed;
    /// <summary>How far along the line (m).</summary>
    public readonly float Distance;

    internal PokeHit(PlayerMovement target, PokeBody body, Vector3 point, Transform bone, PokeZone zone, bool allowed, float distance)
    {
        Target = target;
        Body = body;
        Point = point;
        Bone = bone;
        Zone = zone;
        Allowed = allowed;
        Distance = distance;
    }

    /// <summary>A finger may press here: a zone that gives, on a friend who lets it.</summary>
    public bool Pokes => Zone != PokeZone.None && Allowed;
}

/// <summary>
/// A character as a pointing finger meets it (PointArm, SkinPoke): its surface as points riding its bones, and which part
/// of it a point is (PokeZone).
/// The surface is read off the character's own meshes at run time, any humanoid a modder brings included, the first time
/// a finger comes near it (a slice a frame, Poke.csv Touch.bakeVertices): every vertex of its skin, hair, clothes and worn
/// pieces, with the blend shapes as worn, is put on the bone that moves it most (its bind pose there), and the points
/// are thinned to one per Touch.grid cell. A finger's line then meets the nearest point within Touch.width of it, the
/// bones' bounds first, so it finds the outermost surface: a shirt over a belly, hair over a cheek. Each frame the points
/// ride their bones, so a belly or a breast Magica springs (FleshSpring) is met where it is drawn, not where it rests.
/// The parts come from the humanoid bones: the head (cheeks below the eyes, the top above them, nothing else), the spine
/// (the belly in front, between the hips and well below the chest), the shoulders, upper arms (the shoulder near the
/// joint) and forearms (not the hand). Anything a chest, the hips or the upper chest carries that isn't a humanoid bone
/// (breasts, a bottom, a skirt) is never a zone, so the chest, the bottom and the groin never give.
/// It lives on the body's model, so a body made again (another skin or outfit) is read again.
/// </summary>
[DisallowMultipleComponent]
public sealed class PokeBody : MonoBehaviour
{
    private enum Part { Other, Head, Torso, Off, Shoulder, UpperArm, LowerArm }

    private sealed class Patch
    {
        public Transform Bone;
        public Vector3[] Points;
        public Vector3 Centre;
        public float Radius;
        public Part Part;
        public bool Left;
    }

    private sealed class Gathering
    {
        public readonly List<Vector3> Points = new List<Vector3>();
        public readonly HashSet<long> Cells = new HashSet<long>();
        public float Cell;
    }

    // A worn mesh's shapes beyond this many at once are left out of the surface (a face mid-expression, mostly).
    private const int MostShapes = 64;

    private readonly List<Patch> patches = new List<Patch>();
    private readonly Dictionary<Transform, Gathering> gathering = new Dictionary<Transform, Gathering>();
    private readonly List<Renderer> parts = new List<Renderer>();
    private readonly Dictionary<Transform, (Part part, bool left)> humanoid = new Dictionary<Transform, (Part, bool)>();
    private PlayerMovement player;
    private Animator animator;
    private Transform head, hips, spine, chest, upperChest, neck, leftEye, rightEye;
    private Transform leftUpper, leftLower, leftHand, rightUpper, rightLower, rightHand;
    private Transform eyes;
    // The slice of the read under way: which renderer, its vertices as worn, the bones they ride.
    private int nextPart, cursor;
    private Vector3[] vertices;
    private BoneWeight[] weights;
    private Transform[] bones;
    private Matrix4x4[] binds;
    private Transform rigid;
    private bool baked;
    private int steppedFrame = -1;

    /// <summary>The player this body is.</summary>
    public PlayerMovement Player => player;
    /// <summary>Every renderer of the body (skin, hair, clothes, worn pieces): what a poke on it may bend.</summary>
    internal IReadOnlyList<Renderer> Renderers => parts;

    /// <summary>The body <paramref name="who"/> wears now, read for pokes (made the first time), or null without one.</summary>
    internal static PokeBody Of(PlayerMovement who)
    {
        if (who == null || !who.TryGetComponent(out PlayerBody body)) return null;
        var model = body.BodyAnimator;
        if (model == null || !model.isHuman) return null;
        if (model.TryGetComponent(out PokeBody found)) return found;
        found = model.gameObject.AddComponent<PokeBody>();
        found.Begin(who, model);
        return found;
    }

    private void Begin(PlayerMovement who, Animator model)
    {
        player = who;
        animator = model;
        head = Bone(HumanBodyBones.Head, Part.Head, false);
        hips = Bone(HumanBodyBones.Hips, Part.Torso, false);
        spine = Bone(HumanBodyBones.Spine, Part.Torso, false);
        chest = Bone(HumanBodyBones.Chest, Part.Torso, false);
        upperChest = Bone(HumanBodyBones.UpperChest, Part.Torso, false);
        neck = Bone(HumanBodyBones.Neck, Part.Other, false);
        leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
        rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
        Bone(HumanBodyBones.LeftShoulder, Part.Shoulder, true);
        Bone(HumanBodyBones.RightShoulder, Part.Shoulder, false);
        leftUpper = Bone(HumanBodyBones.LeftUpperArm, Part.UpperArm, true);
        rightUpper = Bone(HumanBodyBones.RightUpperArm, Part.UpperArm, false);
        leftLower = Bone(HumanBodyBones.LeftLowerArm, Part.LowerArm, true);
        rightLower = Bone(HumanBodyBones.RightLowerArm, Part.LowerArm, false);
        leftHand = Bone(HumanBodyBones.LeftHand, Part.Other, true);
        rightHand = Bone(HumanBodyBones.RightHand, Part.Other, false);
        // Every other humanoid bone is a part of no zone (legs, feet, hands, fingers, the neck, the jaw).
        for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
        {
            var found = animator.GetBoneTransform(bone);
            if (found != null && !humanoid.ContainsKey(found)) humanoid[found] = (Part.Other, false);
        }
        Gather();
    }

    private Transform Bone(HumanBodyBones which, Part part, bool left)
    {
        var bone = animator.GetBoneTransform(which);
        if (bone != null) humanoid[bone] = (part, left);
        return bone;
    }

    // Every renderer showing on the body now, the surface read afresh from them.
    private void Gather()
    {
        parts.Clear();
        foreach (var renderer in GetComponentsInChildren<Renderer>(false))
            if (renderer is SkinnedMeshRenderer || renderer is MeshRenderer) parts.Add(renderer);
        patches.Clear();
        gathering.Clear();
        nextPart = cursor = 0;
        vertices = null;
        baked = false;
    }

    /// <summary>
    /// A poke begins on this body: if what it wears changed since it was read (a piece put on or taken off without the
    /// body being made again), it is read again.
    /// </summary>
    internal void Recheck()
    {
        int showing = 0;
        foreach (var renderer in GetComponentsInChildren<Renderer>(false))
            if (renderer is SkinnedMeshRenderer || renderer is MeshRenderer) showing++;
        bool gone = false;
        foreach (var part in parts) if (part == null) { gone = true; break; }
        if (gone || showing != parts.Count) Gather();
    }

    /// <summary>
    /// Reads a slice of the surface (once a frame, Touch.bakeVertices vertices); true once all of it is read and the body
    /// can be met.
    /// </summary>
    internal bool Prepare()
    {
        if (baked) return true;
        if (steppedFrame == Time.frameCount) return false;
        steppedFrame = Time.frameCount;
        int budget = Mathf.Max(1000, (int)PokeTables.Number("Touch.bakeVertices", 40000f));
        float grid = Mathf.Max(.001f, PokeTables.Number("Touch.grid", .006f));
        while (budget > 0)
        {
            if (vertices == null && !Load())
            {
                Finish();
                return true;
            }
            int end = Mathf.Min(vertices.Length, cursor + budget);
            budget -= end - cursor;
            for (; cursor < end; cursor++)
            {
                if (rigid != null)
                {
                    Add(rigid, vertices[cursor], grid);
                    continue;
                }
                int b = weights[cursor].boneIndex0;
                if (b < 0 || b >= bones.Length || b >= binds.Length || bones[b] == null) continue;
                Add(bones[b], binds[b].MultiplyPoint3x4(vertices[cursor]), grid);
            }
            if (cursor >= vertices.Length) Unload();
        }
        return false;
    }

    // The next renderer's mesh, its vertices with the shapes as worn; false when every one is read.
    private bool Load()
    {
        while (nextPart < parts.Count)
        {
            var renderer = parts[nextPart++];
            if (renderer == null || !renderer.enabled) continue;
            cursor = 0;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                var mesh = skinned.sharedMesh;
                if (mesh == null || !mesh.isReadable || mesh.vertexCount == 0) continue;
                vertices = mesh.vertices;
                Shapes(skinned, mesh);
                weights = mesh.boneWeights;
                bones = skinned.bones;
                binds = mesh.bindposes;
                // Not skinned after all: it rides its own transform.
                rigid = bones == null || bones.Length == 0 || weights == null || weights.Length != vertices.Length ? skinned.transform : null;
                return true;
            }
            if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable || mesh.vertexCount == 0) continue;
                vertices = mesh.vertices;
                rigid = renderer.transform;
                return true;
            }
        }
        return false;
    }

    // The blend shapes the renderer wears now (a body option, a face), on the vertices just read: each shape's last frame
    // at its share (most have one frame, at 100).
    private void Shapes(SkinnedMeshRenderer skinned, Mesh mesh)
    {
        int count = mesh.blendShapeCount, used = 0;
        Vector3[] deltas = null, normals = null, tangents = null;
        for (int shape = 0; shape < count && used < MostShapes; shape++)
        {
            float weight = skinned.GetBlendShapeWeight(shape);
            if (Mathf.Abs(weight) < 1f) continue;
            int frames = mesh.GetBlendShapeFrameCount(shape);
            if (frames <= 0) continue;
            float full = mesh.GetBlendShapeFrameWeight(shape, frames - 1);
            if (Mathf.Abs(full) < 1e-3f) continue;
            if (deltas == null)
            {
                deltas = new Vector3[vertices.Length];
                normals = new Vector3[vertices.Length];
                tangents = new Vector3[vertices.Length];
            }
            mesh.GetBlendShapeFrameVertices(shape, frames - 1, deltas, normals, tangents);
            float share = weight / full;
            for (int i = 0; i < vertices.Length; i++) vertices[i] += deltas[i] * share;
            used++;
        }
    }

    private void Unload()
    {
        vertices = null;
        weights = null;
        bones = null;
        binds = null;
        rigid = null;
        cursor = 0;
    }

    private void Add(Transform bone, Vector3 local, float grid)
    {
        if (!gathering.TryGetValue(bone, out var gather))
        {
            gather = new Gathering { Cell = grid / Mathf.Max(1e-6f, MaxScale(bone)) };
            gathering[bone] = gather;
        }
        long x = Mathf.FloorToInt(local.x / gather.Cell), y = Mathf.FloorToInt(local.y / gather.Cell), z = Mathf.FloorToInt(local.z / gather.Cell);
        long key = ((x & 0x1FFFFF) << 42) | ((y & 0x1FFFFF) << 21) | (z & 0x1FFFFF);
        if (gather.Cells.Add(key)) gather.Points.Add(local);
    }

    private void Finish()
    {
        patches.Clear();
        foreach (var pair in gathering)
        {
            var points = pair.Value.Points;
            if (pair.Key == null || points.Count == 0) continue;
            Vector3 centre = Vector3.zero;
            foreach (var point in points) centre += point;
            centre /= points.Count;
            float radius = 0f;
            foreach (var point in points) radius = Mathf.Max(radius, (point - centre).sqrMagnitude);
            var (part, left) = PartOf(pair.Key);
            patches.Add(new Patch { Bone = pair.Key, Points = points.ToArray(), Centre = centre, Radius = Mathf.Sqrt(radius), Part = part, Left = left });
        }
        gathering.Clear();
        baked = true;
    }

    // What part of the body a bone carries: a humanoid bone's own part, else its nearest humanoid ancestor's. What the
    // chest, the upper chest or the hips carry beside the humanoid bones (breasts, a bottom, a skirt) is off.
    private (Part, bool) PartOf(Transform bone)
    {
        if (humanoid.TryGetValue(bone, out var own)) return own;
        for (var up = bone.parent; up != null && up != transform.parent; up = up.parent)
        {
            if (!humanoid.TryGetValue(up, out var carrier)) continue;
            if (up == hips || up == chest || up == upperChest) return (Part.Off, false);
            return carrier;
        }
        return (Part.Other, false);
    }

    /// <summary>
    /// The nearest place on this body's surface along the line from <paramref name="origin"/> (unit
    /// <paramref name="direction"/>), within <paramref name="range"/> (m): where, how far, which bone carries it and which
    /// zone it is. False when the line misses the body or it isn't read yet.
    /// </summary>
    internal bool Raycast(Vector3 origin, Vector3 direction, float range, out Vector3 point, out float distance, out Transform bone, out PokeZone zone)
    {
        point = default;
        bone = null;
        zone = PokeZone.None;
        distance = range;
        if (!baked) return false;
        float width = Mathf.Max(.001f, PokeTables.Number("Touch.width", .007f));
        float width2 = width * width;
        Patch nearest = null;
        foreach (var patch in patches)
        {
            if (patch.Bone == null) continue;
            var matrix = patch.Bone.localToWorldMatrix;
            Vector3 centre = matrix.MultiplyPoint3x4(patch.Centre);
            float radius = patch.Radius * MaxScale(patch.Bone) + width;
            Vector3 toCentre = centre - origin;
            float along = Vector3.Dot(toCentre, direction);
            if (along + radius < 0f || along - radius > distance) continue;
            if (toCentre.sqrMagnitude - along * along > radius * radius) continue;
            foreach (var local in patch.Points)
            {
                Vector3 offset = matrix.MultiplyPoint3x4(local) - origin;
                float t = Vector3.Dot(offset, direction);
                if (t <= 0f || t >= distance) continue;
                if (offset.sqrMagnitude - t * t > width2) continue;
                distance = t;
                nearest = patch;
            }
        }
        if (nearest == null) return false;
        point = origin + direction * distance;
        bone = nearest.Bone;
        zone = ZoneOf(nearest, point);
        return true;
    }

    private PokeZone ZoneOf(Patch patch, Vector3 point)
    {
        switch (patch.Part)
        {
            case Part.Head: return HeadZone(point);
            case Part.Torso: return IsBelly(point) ? PokeZone.Belly : PokeZone.None;
            case Part.Shoulder: return PokeZone.Shoulder;
            case Part.UpperArm:
                return Along(patch.Left ? leftUpper : rightUpper, patch.Left ? leftLower : rightLower, point)
                    < PokeTables.Number("Zone.shoulder", .3f) ? PokeZone.Shoulder : PokeZone.Arm;
            case Part.LowerArm:
                return Along(patch.Left ? leftLower : rightLower, patch.Left ? leftHand : rightHand, point)
                    <= PokeTables.Number("Zone.wrist", .92f) ? PokeZone.Arm : PokeZone.None;
            default: return PokeZone.None;
        }
    }

    // On the head, measured from between the eyes in eye spans along the way the face looks (the player's view, which the
    // head follows): the cheeks below the eyes and beside the nose, the top above them. The forehead, nose, mouth, chin,
    // ears and the back of the head are no zone.
    private PokeZone HeadZone(Vector3 point)
    {
        if (head == null) return PokeZone.None;
        if (eyes == null) eyes = PlayerView.EyesOf(player);
        Vector3 forward = eyes != null ? eyes.forward : animator.transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        if (right.sqrMagnitude < 1e-4f) right = animator.transform.right;
        right.Normalize();
        Vector3 up = Vector3.Cross(forward, right);
        Vector3 offset = (point - EyeMiddle(forward, out float span)) / span;
        float x = Mathf.Abs(Vector3.Dot(offset, right)), y = Vector3.Dot(offset, up), z = Vector3.Dot(offset, forward);
        if (y >= PokeTables.Number("Zone.headTop", 1f)) return PokeZone.HeadTop;
        bool cheek = y <= PokeTables.Number("Zone.cheekTop", -.05f) && y >= PokeTables.Number("Zone.cheekBottom", -1f)
            && x >= PokeTables.Number("Zone.cheekInner", .3f) && x <= PokeTables.Number("Zone.cheekOuter", 1.3f)
            && z >= PokeTables.Number("Zone.cheekFront", -.2f);
        return cheek ? PokeZone.Cheek : PokeZone.None;
    }

    // Between the eyes, and how far apart they are: from the eye bones, else guessed from the head bone and the torso's
    // length (an eye span about an eighth of it).
    private Vector3 EyeMiddle(Vector3 forward, out float span)
    {
        if (leftEye != null && rightEye != null)
        {
            span = Vector3.Distance(leftEye.position, rightEye.position);
            if (span > .01f) return (leftEye.position + rightEye.position) * .5f;
        }
        float torso = hips != null && neck != null ? Vector3.Distance(hips.position, neck.position) : .45f;
        span = Mathf.Max(.02f, torso * .13f);
        return head.position + animator.transform.up * (span * 1.3f) + forward * span;
    }

    // The belly: on the front of the spine, from a little above the hips to well below the chest (Zone.belly*, shares
    // of the hips-to-neck length), not out at the sides.
    private bool IsBelly(Vector3 point)
    {
        if (hips == null || neck == null) return false;
        Vector3 up = neck.position - hips.position;
        float length = up.magnitude;
        if (length < 1e-3f) return false;
        up /= length;
        Vector3 forward = Vector3.ProjectOnPlane(animator.transform.forward, up);
        if (forward.sqrMagnitude < 1e-6f) return false;
        forward.Normalize();
        Vector3 right = Vector3.Cross(up, forward);
        Vector3 offset = point - hips.position;
        float height = Vector3.Dot(offset, up) / length;
        var chestBone = chest != null ? chest : upperChest != null ? upperChest : spine;
        float chestHeight = chestBone != null ? Vector3.Dot(chestBone.position - hips.position, up) / length : .5f;
        float top = Mathf.Min(PokeTables.Number("Zone.bellyHigh", .42f), chestHeight - PokeTables.Number("Zone.bellyBelowChest", .1f));
        if (height < PokeTables.Number("Zone.bellyLow", .1f) || height > top) return false;
        if (Vector3.Dot(offset, forward) <= 0f) return false;
        return Mathf.Abs(Vector3.Dot(offset, right)) / length <= PokeTables.Number("Zone.bellyWide", .3f);
    }

    // How far along a limb from its joint to the next a point lies (0 at the joint, 1 at the next).
    private static float Along(Transform from, Transform to, Vector3 point)
    {
        if (from == null || to == null) return .5f;
        Vector3 limb = to.position - from.position;
        float length2 = limb.sqrMagnitude;
        return length2 < 1e-8f ? .5f : Vector3.Dot(point - from.position, limb) / length2;
    }

    private static float MaxScale(Transform bone)
    {
        Vector3 scale = bone.lossyScale;
        return Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
    }
}
