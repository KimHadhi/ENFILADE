using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Fits the layers of a dressed character to each other from their meshes alone, whoever made them: skin under clothes,
/// underwear under what is worn over it, socks in shoes, hair under a mask. Nothing is written per pair of pieces, so a
/// modder's outfit or character goes with everything else as it is (the user, 2026-11-14: "매번 이렇게 말해 줘서 고칠
/// 수도 없는 노릇이잖아. 유저들이 모드로 의상을 다양한 거 추가하고, 캐릭터도 추가해서 그때마다 다를 텐데 … 근본적이고
/// 유동적으로 이 문제를 해결할 수 있는 똑똑한 방법이 필요해").
/// Two things, both measured on the meshes as the character stands dressed:
/// - Follow: a piece without one of the body's shape keys gets that key made for it from the skin it lies on (FitFollow),
///   so socks that came with no heel key tip down into heeled shoes with the foot, and a shirt from another maker
///   follows a body slider it was never given.
/// - Trim: what of a layer is hidden under the garments over it, or came out through one, is not drawn, and a little of
///   it lying on a garment's rim is tucked in under it (FitTrim). Nothing is pressed about: the press this replaces
///   (UnderwearFit.Press, OutfitDresser.PressUnder) moved every point up to 2.3 cm along its normal to the nearest
///   garment surface, and crushed feet in shoes. The one move left is small and the same everywhere: a layer lies the
///   table's dive (4 mm) under a garment that moves with it, so skin a tight skirt was made almost touching no longer
///   shimmers or pokes through it as the two move.
/// Measuring a layer against a garment is the dear part (tens of milliseconds), so each pair is measured once and kept
/// for as long as both stay as they were, and each outfit's result is kept as a ready mesh: the wardrobe stands the
/// character up again for every try, and a look seen before costs a few lookups.
/// A garment hides for good only what moves with it (FitBones) and lies under it (FitTrim.Rules.On); one whose
/// material is see-through, or that Outfits.csv calls sheer, hides nothing. Whatever goes wrong here leaves the meshes
/// as they were made: the worst a wrong measure does is the clipping there was without it.
/// Numbers: Tables/OutfitFit.
/// </summary>
internal static class OutfitFit
{
    public const string TableName = "OutfitFit";
    // How many of each are kept before the longest unused go: meshes read, poses of them, pairs measured, meshes made
    // (of each kind), and how many vertices the made meshes of a kind may hold together: a body with its seventy keys is
    // megabytes, and a copy of it is made for every outfit that hides another part of it.
    private const int MostSources = 128, MostPoses = 96, MostCuts = 512, MostMaps = 48, MostCopies = 64, MostCopyVertices = 400000;

    /// <summary>A garment worn over a layer, and whether it can hide what is under it (a mask fitted to a face only cuts what comes through it).</summary>
    internal readonly struct Cover
    {
        public readonly Renderer Garment;
        public readonly bool Hides;

        public Cover(Renderer garment, bool hides)
        {
            Garment = garment;
            Hides = hides;
        }
    }

    /// <summary>One layer of the dressed character and everything worn over it.</summary>
    internal sealed class Layer
    {
        public readonly SkinnedMeshRenderer Inner;
        public readonly List<Cover> Over = new List<Cover>();
        /// <summary>
        /// The layer lies snug on what is under it (skin, underwear, a sock): where it lies on a garment's rim it is
        /// tucked in under it. A shirt under a jacket is not: its collar may be meant to lie over the jacket's.
        /// </summary>
        public bool Snug;

        public Layer(SkinnedMeshRenderer inner) => Inner = inner;

        /// <summary>Adds a garment over the layer; named twice, it hides if either naming said so.</summary>
        public void Add(Renderer garment, bool hides)
        {
            for (int i = 0; i < Over.Count; i++)
            {
                if (Over[i].Garment != garment) continue;
                if (hides && !Over[i].Hides) Over[i] = new Cover(garment, true);
                return;
            }
            Over.Add(new Cover(garment, hides));
        }
    }

    /// <summary>A piece that may follow the body's shape keys: its renderer, the keys it holds itself and whether it is the character's own.</summary>
    internal readonly struct Follower
    {
        public readonly SkinnedMeshRenderer Renderer;
        /// <summary>Keys the piece sets on the body itself (a shoe's heel): the body has them for the piece, which doesn't follow them.</summary>
        public readonly HashSet<string> Holds;
        /// <summary>The character's own piece, made on the body as it came; an outfit's is measured for which body it was made on.</summary>
        public readonly bool Own;

        public Follower(SkinnedMeshRenderer renderer, HashSet<string> holds, bool own)
        {
            Renderer = renderer;
            Holds = holds;
            Own = own;
        }
    }

    /// <summary>A mesh made here, kept while any dressed model wears it (OutfitMeshes counts the wearers).</summary>
    internal sealed class Kept
    {
        internal Mesh Mesh;
        /// <summary>For each of its vertices, whether a triangle still draws it; null when all are.</summary>
        internal bool[] Drawn;
        internal int Wearers;
        internal long Used;
        internal int Vertices;
        /// <summary>Whether a mesh was made at all: a layer the outfit leaves as it is has none, and that is kept too.</summary>
        internal bool Changed;
    }

    private struct Values
    {
        public FitTrim.Rules Rules;
        public bool Skin, Follow;
        public float FollowReach, FollowLeast, FollowClearly, SheerAlpha;
        public int Stamp;
    }

    // ---- What a mesh is, read once ----

    private sealed class Source
    {
        public Mesh Mesh;
        public Vector3[] Made;
        // Its triangle sub-meshes one after another; per sub-mesh the first triangle and how many (-1: not triangles).
        public int[] Triangles, SubFirst, SubCount;
        // Per vertex and per triangle the bone followed most (the mesh's own bone numbers); an unskinned mesh has one.
        public int[] Bone, TriangleBone, UsedBones;
        public int BoneCount;
        public bool Skinned;
        public FitShape Shape;
        public int[] PointBone;
        public long Used;
    }

    // A mesh where a renderer puts it with its keys as they are, in the model's space.
    private sealed class Pose
    {
        public Vector3[] At, Points, Normals;
        public Vector3 Min, Max;
        public Matrix4x4 Matrix;
        public long State, Used;
        public FitBvh Tree;
    }

    private sealed class Measured
    {
        public FitTrim.Cut Cut;
        public long Id, Used;
    }

    // Which skin a garment's vertices lie on (FitFollow.Map), and per body key the garment's own move for it (null: none
    // worth making) and which body the garment was made on (FitFollow.MadeOn).
    private sealed class Followed
    {
        public FitFollow.Map Map;
        public Vector3[] Garment, Body;
        public readonly Dictionary<int, Vector3[]> Moves = new Dictionary<int, Vector3[]>();
        public readonly Dictionary<int, int> MadeOn = new Dictionary<int, int>();
        public long Used;
    }

    // The dressed model's bones as numbers (FitBones), made only when something has to be measured afresh.
    private sealed class Bones
    {
        private readonly Dictionary<Transform, int> ids = new Dictionary<Transform, int>();
        private readonly bool[] body;
        public readonly FitBones Rules;

        public Bones(Transform root, Animator animator)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) ids[all[i]] = i;
            var parent = new int[all.Length];
            body = new bool[all.Length];
            for (int i = 0; i < all.Length; i++)
                parent[i] = all[i].parent != null && ids.TryGetValue(all[i].parent, out int above) ? above : -1;
            if (animator != null && animator.isHuman)
                for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
                {
                    var at = animator.GetBoneTransform(bone);
                    if (at != null && ids.TryGetValue(at, out int id)) body[id] = true;
                }
            Rules = new FitBones(parent, body);
        }

        public int Of(Transform bone) => bone != null && ids.TryGetValue(bone, out int id) ? id : -1;

        // The body bone an unskinned piece is carried by (a mask on the head): itself or the first above it.
        public int Carrier(Transform piece)
        {
            for (var at = piece; at != null; at = at.parent)
                if (ids.TryGetValue(at, out int id) && body[id]) return id;
            return Of(piece);
        }
    }

    private static Values? values;
    private static bool listening;
    private static long clock, measuredIds;
    private static int stamp;
    private static readonly Dictionary<int, Source> sources = new Dictionary<int, Source>();
    private static readonly Dictionary<(int mesh, long state), Pose> poses = new Dictionary<(int, long), Pose>();
    private static readonly Dictionary<(int inner, int outer, long outerState, long region, bool hides, int stamp), Measured> cuts =
        new Dictionary<(int, int, long, long, bool, int), Measured>();
    private static readonly Dictionary<(int mesh, long cuts, int stamp), Kept> trimmed = new Dictionary<(int, long, int), Kept>();
    private static readonly Dictionary<(int garment, int body, int stamp), Followed> maps = new Dictionary<(int, int, int), Followed>();
    private static readonly Dictionary<(int mesh, int body, long keys, int stamp), Kept> keyed = new Dictionary<(int, int, long, int), Kept>();
    private static readonly Dictionary<int, bool[]> drawnOf = new Dictionary<int, bool[]>();
    // Where each mesh has been placed in a model (a mesh has one place per character it is worn on, a nudged mask a few).
    private static readonly Dictionary<int, List<Matrix4x4>> placements = new Dictionary<int, List<Matrix4x4>>();
    // Per body mesh and key: the box round the vertices the key moves, in the mesh's space (any false: it moves none).
    private static readonly Dictionary<(int mesh, int key), (Bounds box, bool any)> keyBoxes = new Dictionary<(int, int), (Bounds, bool)>();
    private static readonly Dictionary<int, bool> sheerOf = new Dictionary<int, bool>();
    private static readonly Dictionary<string, HashSet<string>> optionKeys = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> warned = new HashSet<string>();
    private static Vector3[] moves = Array.Empty<Vector3>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        // Meshes made in the play that ended went with it; nothing here points at them any more.
        sources.Clear();
        poses.Clear();
        cuts.Clear();
        trimmed.Clear();
        maps.Clear();
        keyed.Clear();
        drawnOf.Clear();
        placements.Clear();
        keyBoxes.Clear();
        sheerOf.Clear();
        optionKeys.Clear();
        values = null;
        stamp = 0;
    }

    // The tables were saved again: what was measured under the old numbers is no longer asked for (its stamp is old)
    // and goes as the caches turn over.
    private static void Forget()
    {
        values = null;
        optionKeys.Clear();
        stamp++;
    }

    private static Values Settings()
    {
        if (values.HasValue) return values.Value;
        if (!listening)
        {
            GameTables.Reloaded += Forget;
            listening = true;
        }
        var read = new Values
        {
            Rules = new FitTrim.Rules
            {
                Near = .03f, Behind = .025f, On = .03f, Facing = .05f, Margin = 2, Inset = .002f, Dive = .004f, Tuck = .006f, TuckShare = .6f
            },
            Skin = true, Follow = true, FollowReach = .03f, FollowLeast = .0005f, FollowClearly = .6f, SheerAlpha = .9f, Stamp = stamp
        };
        if (GameTables.TryGetRow(TableName, "Default", out var cells))
        {
            read.Rules.Near = Mathf.Max(.001f, cells.Number("near", read.Rules.Near));
            read.Rules.Behind = Mathf.Max(0f, cells.Number("behind", read.Rules.Behind));
            read.Rules.On = Mathf.Max(0f, cells.Number("on", read.Rules.On));
            read.Rules.Facing = Mathf.Clamp(cells.Number("facing", read.Rules.Facing), 0f, .9f);
            read.Rules.Margin = Mathf.Max(0, cells.Whole("margin", read.Rules.Margin));
            read.Rules.Inset = Mathf.Max(0f, cells.Number("inset", read.Rules.Inset));
            read.Rules.Dive = Mathf.Max(0f, cells.Number("dive", read.Rules.Dive));
            read.Rules.Tuck = Mathf.Max(0f, cells.Number("tuck", read.Rules.Tuck));
            read.Rules.TuckShare = Mathf.Clamp01(cells.Number("tuckShare", read.Rules.TuckShare));
            read.Skin = cells.Flag("skin", read.Skin);
            read.Follow = cells.Flag("follow", read.Follow);
            read.FollowReach = Mathf.Max(.001f, cells.Number("followReach", read.FollowReach));
            read.FollowLeast = Mathf.Max(0f, cells.Number("followLeast", read.FollowLeast));
            read.FollowClearly = Mathf.Clamp01(cells.Number("followClearly", read.FollowClearly));
            read.SheerAlpha = Mathf.Clamp01(cells.Number("sheerAlpha", read.SheerAlpha));
        }
        values = read;
        return read;
    }

    /// <summary>Whether the skin is fitted under clothes too (OutfitFit.csv skin), not only clothes under clothes.</summary>
    internal static bool FitsSkin => Settings().Skin;

    /// <summary>
    /// Whether a renderer is drawn see-through, so that what lies under it shows: one of its materials is a cut-out or
    /// see-through one whose own colour is clearly less than solid. A texture's own see-through parts can't be read from
    /// here; a piece sheer by its texture says so in Outfits.csv (sheer).
    /// </summary>
    internal static bool Sheer(Renderer renderer)
    {
        if (renderer == null) return false;
        float least = Settings().SheerAlpha;
        foreach (var material in renderer.sharedMaterials)
        {
            if (material == null) continue;
            int id = material.GetInstanceID();
            if (!sheerOf.TryGetValue(id, out bool sheer))
            {
                var look = MaterialLook.Of(material);
                sheerOf[id] = sheer = look.Cut > 0f && look.Color.a < least;
            }
            if (sheer) return true;
        }
        return false;
    }

    /// <summary>
    /// For a mesh made here with triangles left out, which of its vertices are still drawn; null for any other mesh.
    /// What measures a dressed body's outline from its vertices (DrawnSole) skips the rest: a sock's sole that came out
    /// under a boot is no longer drawn, and the body must not stand on it.
    /// </summary>
    internal static bool[] DrawnVertices(Mesh mesh) =>
        mesh != null && drawnOf.TryGetValue(mesh.GetInstanceID(), out var drawn) ? drawn : null;

    // ---- Trim ----

    /// <summary>
    /// Leaves out of each layer what the garments over it hide or what came through them, and tucks in what lies on
    /// their rims (FitTrim), on meshes kept for every model dressed the same. Call last, with every key where dressing
    /// leaves it.
    /// </summary>
    internal static void Trim(GameObject model, Animator animator, List<Layer> layers)
    {
        if (model == null || layers == null || layers.Count == 0) return;
        var settings = Settings();
        long began = WardrobeCost.Begin();
        try
        {
            Bones bones = null;
            var toModel = model.transform.worldToLocalMatrix;
            var found = new List<FitTrim.Cut>();
            var ids = new List<long>();
            foreach (var layer in layers)
            {
                var inner = layer.Inner;
                var mesh = inner != null ? inner.sharedMesh : null;
                var source = mesh != null ? SourceOf(mesh) : null;
                if (source == null || !source.Skinned || source.Triangles.Length == 0) continue;
                var pose = PoseOf(inner, mesh, source, toModel);
                if (pose.Points == null)
                {
                    source.Shape ??= ShapeOf(source);
                    pose.Points = source.Shape.Points(pose.At);
                    pose.Normals = source.Shape.Normals(pose.Points);
                }
                found.Clear();
                ids.Clear();
                foreach (var cover in layer.Over)
                {
                    var garment = cover.Garment;
                    if (garment == null || garment == inner) continue;
                    var garmentMesh = MeshTools.MeshOf(garment);
                    var garmentSource = garmentMesh != null ? SourceOf(garmentMesh) : null;
                    if (garmentSource == null || garmentSource.Triangles.Length == 0) continue;
                    var garmentPose = PoseOf(garment, garmentMesh, garmentSource, toModel);
                    Vector3 low = garmentPose.Min - Vector3.one * settings.Rules.Near, high = garmentPose.Max + Vector3.one * settings.Rules.Near;
                    if (pose.Max.x < low.x || pose.Max.y < low.y || pose.Max.z < low.z || pose.Min.x > high.x || pose.Min.y > high.y || pose.Min.z > high.z)
                        continue;
                    long region = Region(pose.Points, low, high);
                    if (region == 0L) continue;
                    var key = (mesh.GetInstanceID(), garmentMesh.GetInstanceID(), garmentPose.State, region, cover.Hides, settings.Stamp);
                    if (!cuts.TryGetValue(key, out var measured))
                    {
                        bones ??= new Bones(model.transform, animator);
                        var rows = Rows(bones, inner, source, garment, garmentSource);
                        garmentPose.Tree ??= new FitBvh(garmentPose.At, garmentSource.Triangles, garmentSource.TriangleBone);
                        long measureBegan = WardrobeCost.Begin();
                        var cut = FitTrim.Measure(source.Shape, pose.Points, pose.Normals, source.PointBone, garmentPose.Tree, rows, cover.Hides, settings.Rules);
                        WardrobeCost.End(WardrobeCost.Part.Measure, measureBegan);
                        WardrobeCost.Count(WardrobeCost.Tally.Measured);
                        measured = new Measured { Cut = cut, Id = ++measuredIds };
                        Keep(cuts, key, measured, MostCuts, item => item.Used);
                    }
                    else WardrobeCost.Count(WardrobeCost.Tally.Remembered);
                    measured.Used = ++clock;
                    if (measured.Cut == null) continue;
                    found.Add(measured.Cut);
                    ids.Add(measured.Id);
                }
                if (found.Count == 0) continue;
                var rules = settings.Rules;
                if (!layer.Snug) rules.Tuck = 0f;
                ids.Sort();
                long all = 1469598103934665603L;
                foreach (long id in ids) all = (all ^ id) * 1099511628211L;
                all = (all ^ (layer.Snug ? 1L : 2L)) * 1099511628211L;
                var copyKey = (mesh.GetInstanceID(), all, settings.Stamp);
                if (!trimmed.TryGetValue(copyKey, out var kept) || (kept.Changed && kept.Mesh == null))
                {
                    var result = FitTrim.Solve(source.Shape, pose.Points, pose.Normals, found, rules);
                    kept = new Kept { Changed = result.Any };
                    if (result.Any) Cut(mesh, source, pose, result, kept);
                    KeepMesh(trimmed, copyKey, kept);
                    WardrobeCost.Count(WardrobeCost.Tally.Meshes);
                }
                kept.Used = ++clock;
                if (kept.Mesh == null) continue;
                Wear(inner, kept.Mesh);
                OutfitMeshes.Hold(model, kept);
            }
        }
        catch (Exception error)
        {
            // The pieces stay as they were made: clipping, as before there was a fit, and never a broken body.
            Warn("옷 겹침 맞춤이 멈췄습니다. 옷은 만든 그대로 입습니다: " + error);
        }
        WardrobeCost.End(WardrobeCost.Part.Fit, began);
    }

    // A number for where the layer's points inside a garment's reach stand (to a tenth of a millimetre): the same number
    // means the pair is as it was when measured. 0 when no point is inside.
    private static long Region(Vector3[] points, Vector3 low, Vector3 high)
    {
        long hash = 1469598103934665603L;
        int inside = 0;
        for (int p = 0; p < points.Length; p++)
        {
            Vector3 at = points[p];
            if (at.x < low.x || at.y < low.y || at.z < low.z || at.x > high.x || at.y > high.y || at.z > high.z) continue;
            inside++;
            hash = (hash ^ p) * 1099511628211L;
            hash = (hash ^ Mathf.RoundToInt(at.x * 1e4f)) * 1099511628211L;
            hash = (hash ^ Mathf.RoundToInt(at.y * 1e4f)) * 1099511628211L;
            hash = (hash ^ Mathf.RoundToInt(at.z * 1e4f)) * 1099511628211L;
        }
        if (inside == 0) return 0L;
        return hash == 0L ? 1L : hash;
    }

    // By the layer's own bone numbers, which of the garment's bones move with each (FitBones), for the bones the layer uses.
    private static bool[][] Rows(Bones bones, SkinnedMeshRenderer inner, Source source, Renderer garment, Source garmentSource)
    {
        var innerBones = inner.bones;
        int[] garmentIds;
        if (garmentSource.Skinned && garment is SkinnedMeshRenderer skinned)
        {
            var garmentBones = skinned.bones;
            garmentIds = new int[garmentSource.BoneCount];
            for (int i = 0; i < garmentIds.Length; i++) garmentIds[i] = i < garmentBones.Length ? bones.Of(garmentBones[i]) : -1;
        }
        else garmentIds = new[] { bones.Carrier(garment.transform) };
        var used = new int[source.UsedBones.Length];
        for (int i = 0; i < used.Length; i++)
        {
            int local = source.UsedBones[i];
            used[i] = local < innerBones.Length ? bones.Of(innerBones[local]) : -1;
        }
        bones.Rules.Ready(used);
        var rows = new bool[source.BoneCount][];
        for (int i = 0; i < used.Length; i++)
        {
            if (used[i] < 0) continue;
            var whole = bones.Rules.Rows[used[i]];
            var row = new bool[garmentIds.Length];
            for (int k = 0; k < row.Length; k++) row[k] = garmentIds[k] >= 0 && whole[garmentIds[k]];
            rows[source.UsedBones[i]] = row;
        }
        return rows;
    }

    // The mesh again without the triangles that went and with the points that moved, its keys and everything else as it was.
    private static void Cut(Mesh mesh, Source source, Pose pose, FitTrim.Result result, Kept kept)
    {
        var copy = Object.Instantiate(mesh);
        copy.name = mesh.name;
        var drawn = new bool[source.Made.Length];
        var left = new List<int>();
        for (int sub = 0; sub < source.SubCount.Length; sub++)
        {
            int count = source.SubCount[sub];
            if (count < 0) continue;
            left.Clear();
            bool changed = false;
            for (int t = source.SubFirst[sub]; t < source.SubFirst[sub] + count; t++)
            {
                if (result.Gone[t])
                {
                    changed = true;
                    continue;
                }
                for (int corner = 0; corner < 3; corner++)
                {
                    int vertex = source.Triangles[t * 3 + corner];
                    left.Add(vertex);
                    drawn[vertex] = true;
                }
            }
            if (changed) copy.SetTriangles(left, sub, false);
        }
        if (result.Moved.Length > 0)
        {
            var to = new Dictionary<int, Vector3>(result.Moved.Length);
            for (int i = 0; i < result.Moved.Length; i++) to[result.Moved[i]] = result.MovedTo[i];
            var vertices = (Vector3[])source.Made.Clone();
            var back = pose.Matrix.inverse;
            for (int v = 0; v < vertices.Length; v++)
            {
                int point = source.Shape.Weld[v];
                if (to.TryGetValue(point, out var place)) vertices[v] += back.MultiplyVector(place - pose.Points[point]);
            }
            copy.vertices = vertices;
        }
        kept.Mesh = copy;
        kept.Vertices = source.Made.Length;
        kept.Drawn = result.GoneCount > 0 ? drawn : null;
        if (kept.Drawn != null) drawnOf[copy.GetInstanceID()] = kept.Drawn;
    }

    // ---- Follow ----

    /// <summary>
    /// Gives each piece the body's shape keys it lacks and sets them as the body has them (FitFollow): the keys that are
    /// not as the piece was made for (a heel on, a slider moved), and the keys the wardrobe's body page can slide, so a
    /// slider moves the piece at once. Call once the body's keys are where dressing leaves them, before anything is
    /// measured on the dressed meshes.
    /// </summary>
    /// <param name="skin">The body: the character's own skinned meshes that are skin, not pieces and not on the head.</param>
    /// <param name="made">The body's keys as the character came (by name, those not 0).</param>
    internal static void Follow(GameObject model, string skinId, Animator animator, List<SkinnedMeshRenderer> skin, List<Follower> pieces,
        IReadOnlyDictionary<string, float> made)
    {
        if (model == null || skin == null || pieces == null || skin.Count == 0 || pieces.Count == 0) return;
        var settings = Settings();
        if (!settings.Follow) return;
        long began = WardrobeCost.Begin();
        try
        {
            Bones bones = null;
            var toModel = model.transform.worldToLocalMatrix;
            var options = OptionKeys(skinId);
            var wanted = new List<(int key, string name, float now, float madeAt, bool slides)>();
            var taken = new List<(string name, Vector3[] moves, float from, float now)>();
            foreach (var body in skin)
            {
                var bodyMesh = body != null ? body.sharedMesh : null;
                if (bodyMesh == null || bodyMesh.blendShapeCount == 0) continue;
                wanted.Clear();
                for (int key = 0; key < bodyMesh.blendShapeCount; key++)
                {
                    string name = bodyMesh.GetBlendShapeName(key);
                    if (SkinCover.Hides(name)) continue;
                    float now = body.GetBlendShapeWeight(key);
                    float madeAt = made != null && made.TryGetValue(name, out float before) ? before : 0f;
                    bool slides = options.Contains(name);
                    if (now != 0f || madeAt != 0f || slides) wanted.Add((key, name, now, madeAt, slides));
                }
                if (wanted.Count == 0) continue;
                var bodySource = SourceOf(bodyMesh);
                if (bodySource == null) continue;
                var bodyMatrix = Placement(bodyMesh, toModel * body.transform.localToWorldMatrix, out _);
                foreach (var piece in pieces)
                {
                    var renderer = piece.Renderer;
                    var mesh = renderer != null ? renderer.sharedMesh : null;
                    if (mesh == null || renderer == body) continue;
                    taken.Clear();
                    Source source = null;
                    Followed followed = null;
                    var matrix = Placement(mesh, toModel * renderer.transform.localToWorldMatrix, out _);
                    var box = MeshTools.BoxOf(mesh.bounds, matrix);
                    box.Expand(settings.FollowReach * 2f);
                    foreach (var (key, name, now, madeAt, slides) in wanted)
                    {
                        if (mesh.GetBlendShapeIndex(name) >= 0 || (piece.Holds != null && piece.Holds.Contains(name))) continue;
                        // A key that moves nothing near the piece is not its concern (a heel and a jacket).
                        if (!MovesNear(bodyMesh, bodySource, key, bodyMatrix, box)) continue;
                        if (followed == null)
                        {
                            source = SourceOf(mesh);
                            if (source == null) break;
                            var mapKey = (mesh.GetInstanceID(), bodyMesh.GetInstanceID(), settings.Stamp);
                            if (!maps.TryGetValue(mapKey, out followed))
                            {
                                bones ??= new Bones(model.transform, animator);
                                followed = Lay(bones, renderer, source, matrix, body, bodySource, bodyMatrix, settings);
                                Keep(maps, mapKey, followed, MostMaps, item => item.Used);
                            }
                            followed.Used = ++clock;
                        }
                        if (!followed.Moves.TryGetValue(key, out var garmentMoves))
                        {
                            garmentMoves = MovesFor(followed, bodyMesh, key, bodyMatrix, matrix, settings);
                            followed.Moves[key] = garmentMoves;
                        }
                        if (garmentMoves == null) continue;
                        // The body the piece was made on. The character's own: the character as it came. An outfit's,
                        // for a key the character comes with on: whichever body its mesh lies on (socks made on the flat
                        // foot of a character that comes in heels), and where the meshes can't tell, it is left alone.
                        float from = madeAt;
                        if (madeAt != 0f && !piece.Own)
                        {
                            if (!followed.MadeOn.TryGetValue(key, out int madeOn))
                                followed.MadeOn[key] = madeOn = MadeOn(followed, bodyMesh, key, madeAt, bodyMatrix, settings);
                            if (madeOn == 0) continue;
                            if (madeOn < 0) from = 0f;
                        }
                        if (now == from && !slides) continue;
                        taken.Add((name, garmentMoves, from, now));
                    }
                    if (taken.Count == 0) continue;
                    long all = 1469598103934665603L;
                    foreach (var (name, _, from, _) in taken)
                    {
                        all = (all ^ name.GetHashCode()) * 1099511628211L;
                        all = (all ^ Mathf.RoundToInt(from * 10f)) * 1099511628211L;
                    }
                    var copyKey = (mesh.GetInstanceID(), bodyMesh.GetInstanceID(), all, settings.Stamp);
                    if (!keyed.TryGetValue(copyKey, out var kept) || kept.Mesh == null)
                    {
                        kept = new Kept { Mesh = Key(mesh, source, taken), Changed = true, Vertices = source.Made.Length };
                        KeepMesh(keyed, copyKey, kept);
                        WardrobeCost.Count(WardrobeCost.Tally.Keys, taken.Count);
                    }
                    kept.Used = ++clock;
                    Wear(renderer, kept.Mesh);
                    OutfitMeshes.Hold(model, kept);
                    foreach (var (name, _, _, now) in taken)
                    {
                        int index = kept.Mesh.GetBlendShapeIndex(name);
                        if (index >= 0) renderer.SetBlendShapeWeight(index, now);
                    }
                }
            }
        }
        catch (Exception error)
        {
            Warn("옷이 몸의 셰이프키를 따라가게 하다가 멈췄습니다. 옷은 만든 그대로 입습니다: " + error);
        }
        WardrobeCost.End(WardrobeCost.Part.Follow, began);
    }

    // Whether a key moves any of the body within a box in the model's space: by the box round what it moves, read once.
    private static bool MovesNear(Mesh bodyMesh, Source bodySource, int key, Matrix4x4 bodyMatrix, Bounds box)
    {
        var id = (bodyMesh.GetInstanceID(), key);
        if (!keyBoxes.TryGetValue(id, out var moved))
        {
            moved = default;
            int frames = bodyMesh.GetBlendShapeFrameCount(key);
            if (frames > 0)
            {
                if (moves.Length != bodyMesh.vertexCount) moves = new Vector3[bodyMesh.vertexCount];
                bodyMesh.GetBlendShapeFrameVertices(key, frames - 1, moves, null, null);
                for (int i = 0; i < moves.Length && i < bodySource.Made.Length; i++)
                {
                    if (moves[i].sqrMagnitude < 1e-8f) continue;
                    if (!moved.any) moved = (new Bounds(bodySource.Made[i], Vector3.zero), true);
                    else moved.box.Encapsulate(bodySource.Made[i]);
                }
            }
            keyBoxes[id] = moved;
        }
        return moved.any && MeshTools.BoxOf(moved.box, bodyMatrix).Intersects(box);
    }

    // The keys the wardrobe's body page can move on this character (BodyShapes.csv).
    private static HashSet<string> OptionKeys(string skinId)
    {
        skinId ??= "";
        if (optionKeys.TryGetValue(skinId, out var keys)) return keys;
        keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in BodyShapes.For(skinId))
            foreach (var choice in option.Keys)
                foreach (string key in choice) keys.Add(key);
        return optionKeys[skinId] = keys;
    }

    // Which skin the garment's vertices lie on, both as made and placed in the model.
    private static Followed Lay(Bones bones, SkinnedMeshRenderer garment, Source source, Matrix4x4 matrix, SkinnedMeshRenderer body,
        Source bodySource, Matrix4x4 bodyMatrix, Values settings)
    {
        var followed = new Followed { Garment = Placed(source.Made, matrix), Body = Placed(bodySource.Made, bodyMatrix) };
        bool[][] rows = null;
        if (source.Skinned && bodySource.Skinned) rows = Rows(bones, garment, source, body, bodySource);
        long began = WardrobeCost.Begin();
        followed.Map = FitFollow.Build(followed.Garment, rows != null ? source.Bone : null, followed.Body, rows != null ? bodySource.Bone : null,
            rows, settings.FollowReach);
        WardrobeCost.End(WardrobeCost.Part.Measure, began);
        return followed;
    }

    private static Vector3[] Placed(Vector3[] made, Matrix4x4 matrix)
    {
        var placed = new Vector3[made.Length];
        for (int i = 0; i < made.Length; i++) placed[i] = matrix.MultiplyPoint3x4(made[i]);
        return placed;
    }

    // The garment's own move for one of the body's keys, in the garment mesh's space; null when it moves less than counts.
    private static Vector3[] MovesFor(Followed followed, Mesh bodyMesh, int key, Matrix4x4 bodyMatrix, Matrix4x4 matrix, Values settings)
    {
        var bodyMoves = KeyMoves(bodyMesh, key, bodyMatrix);
        if (bodyMoves == null) return null;
        var garmentMoves = new Vector3[followed.Garment.Length];
        if (FitFollow.Moves(followed.Map, bodyMoves, garmentMoves) < settings.FollowLeast) return null;
        var back = matrix.inverse;
        for (int i = 0; i < garmentMoves.Length; i++) garmentMoves[i] = back.MultiplyVector(garmentMoves[i]);
        return garmentMoves;
    }

    // A key's full move of each of the body's vertices, in the model's space (a buffer used over: read it at once).
    private static Vector3[] KeyMoves(Mesh bodyMesh, int key, Matrix4x4 bodyMatrix)
    {
        int frames = bodyMesh.GetBlendShapeFrameCount(key);
        if (frames == 0) return null;
        if (moves.Length != bodyMesh.vertexCount) moves = new Vector3[bodyMesh.vertexCount];
        bodyMesh.GetBlendShapeFrameVertices(key, frames - 1, moves, null, null);
        // A key's last frame is its full move, whatever weight the frame is filed at.
        for (int i = 0; i < moves.Length; i++) moves[i] = bodyMatrix.MultiplyVector(moves[i]);
        return moves;
    }

    private static int MadeOn(Followed followed, Mesh bodyMesh, int key, float weight, Matrix4x4 bodyMatrix, Values settings)
    {
        var bodyMoves = KeyMoves(bodyMesh, key, bodyMatrix);
        if (bodyMoves == null) return 0;
        var on = new Vector3[followed.Body.Length];
        float share = weight / 100f;
        for (int i = 0; i < on.Length; i++) on[i] = followed.Body[i] + bodyMoves[i] * share;
        return FitFollow.MadeOn(followed.Map, followed.Garment, followed.Body, on, settings.FollowReach, settings.FollowClearly);
    }

    // The garment's mesh again with the keys it lacked: each key's move as its own shape key under the body's name, and
    // the mesh as made taken back by the keys the garment was made with on, so the key at that weight is the mesh as made.
    private static Mesh Key(Mesh mesh, Source source, List<(string name, Vector3[] moves, float from, float now)> taken)
    {
        var copy = Object.Instantiate(mesh);
        copy.name = mesh.name;
        Vector3[] vertices = null;
        foreach (var (name, garmentMoves, from, _) in taken)
        {
            if (from != 0f)
            {
                vertices ??= (Vector3[])source.Made.Clone();
                float share = from / 100f;
                for (int i = 0; i < vertices.Length; i++) vertices[i] -= garmentMoves[i] * share;
            }
            copy.AddBlendShapeFrame(name, 100f, garmentMoves, null, null);
        }
        if (vertices != null) copy.vertices = vertices;
        return copy;
    }

    // ---- Reading meshes ----

    private static Source SourceOf(Mesh mesh)
    {
        int id = mesh.GetInstanceID();
        if (sources.TryGetValue(id, out var known) && known.Mesh == mesh)
        {
            known.Used = ++clock;
            return known.Made != null ? known : null;
        }
        var source = new Source { Mesh = mesh, Used = ++clock };
        if (!mesh.isReadable) Warn($"{mesh.name}: 메시를 읽을 수 없어(Read/Write 꺼짐) 다른 옷과 겹치는 곳을 맞추지 못합니다.");
        else
        {
            source.Made = mesh.vertices;
            int subs = mesh.subMeshCount;
            source.SubFirst = new int[subs];
            source.SubCount = new int[subs];
            var all = new List<int>();
            var one = new List<int>();
            for (int sub = 0; sub < subs; sub++)
            {
                source.SubFirst[sub] = all.Count / 3;
                if (mesh.GetTopology(sub) != MeshTopology.Triangles)
                {
                    source.SubCount[sub] = -1;
                    continue;
                }
                mesh.GetTriangles(one, sub);
                source.SubCount[sub] = one.Count / 3;
                all.AddRange(one);
            }
            source.Triangles = all.ToArray();
            int triangles = source.Triangles.Length / 3;
            source.TriangleBone = new int[triangles];
            var perVertex = mesh.GetBonesPerVertex();
            if (perVertex.Length == source.Made.Length && source.Made.Length > 0)
            {
                // A vertex's weights come largest first.
                var weights = mesh.GetAllBoneWeights();
                source.Bone = new int[source.Made.Length];
                int at = 0, most = 0;
                for (int v = 0; v < source.Bone.Length; v++)
                {
                    int count = perVertex[v];
                    source.Bone[v] = count > 0 && at < weights.Length ? weights[at].boneIndex : -1;
                    if (source.Bone[v] > most) most = source.Bone[v];
                    at += count;
                }
                source.BoneCount = most + 1;
                source.Skinned = true;
                for (int t = 0; t < triangles; t++) source.TriangleBone[t] = source.Bone[source.Triangles[t * 3]];
                var used = new HashSet<int>();
                foreach (int bone in source.Bone)
                    if (bone >= 0) used.Add(bone);
                source.UsedBones = new int[used.Count];
                used.CopyTo(source.UsedBones);
            }
            else
            {
                source.BoneCount = 1;
                source.UsedBones = Array.Empty<int>();
            }
        }
        if (sources.Count >= MostSources) Shed(sources, MostSources * 3 / 4, item => item.Used, item => item.Mesh == null);
        sources[id] = source;
        return source.Made != null ? source : null;
    }

    private static FitShape ShapeOf(Source source)
    {
        var shape = new FitShape(source.Made, source.Triangles);
        source.PointBone = new int[shape.Count];
        for (int p = 0; p < shape.Count; p++) source.PointBone[p] = source.Bone[shape.Sample[p]];
        return shape;
    }

    // Where the renderer puts its mesh, keys on, in the model's space; kept by the keys' weights, so a model made again
    // the same reads nothing twice.
    private static Pose PoseOf(Renderer renderer, Mesh mesh, Source source, Matrix4x4 toModel)
    {
        var matrix = Placement(mesh, toModel * renderer.transform.localToWorldMatrix, out int place);
        long state = 1469598103934665603L;
        var skinned = renderer as SkinnedMeshRenderer;
        int keys = skinned != null ? mesh.blendShapeCount : 0;
        for (int key = 0; key < keys; key++)
        {
            float weight = skinned.GetBlendShapeWeight(key);
            if (weight == 0f) continue;
            state = (state ^ key) * 1099511628211L;
            state = (state ^ Mathf.RoundToInt(weight * 100f)) * 1099511628211L;
        }
        state = (state ^ place) * 1099511628211L;
        var poseKey = (mesh.GetInstanceID(), state);
        if (poses.TryGetValue(poseKey, out var pose))
        {
            pose.Used = ++clock;
            return pose;
        }
        var at = (Vector3[])source.Made.Clone();
        for (int key = 0; key < keys; key++)
        {
            float weight = skinned.GetBlendShapeWeight(key);
            int frame = mesh.GetBlendShapeFrameCount(key) - 1;
            if (weight == 0f || frame < 0) continue;
            float full = mesh.GetBlendShapeFrameWeight(key, frame);
            if (full == 0f) continue;
            if (moves.Length != at.Length) moves = new Vector3[at.Length];
            mesh.GetBlendShapeFrameVertices(key, frame, moves, null, null);
            float share = weight / full;
            for (int i = 0; i < at.Length; i++) at[i] += moves[i] * share;
        }
        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        for (int i = 0; i < at.Length; i++)
        {
            Vector3 placed = matrix.MultiplyPoint3x4(at[i]);
            at[i] = placed;
            min = Vector3.Min(min, placed);
            max = Vector3.Max(max, placed);
        }
        pose = new Pose { At = at, Min = min, Max = max, Matrix = matrix, State = state, Used = ++clock };
        Keep(poses, poseKey, pose, MostPoses, item => item.Used);
        return pose;
    }

    // The place a mesh has in the model, as the very numbers it was first seen with there. A model made again stands a
    // hair off where the last one stood (its place is worked out through other parents), and vertices placed with
    // numbers that differ in their last digit would look moved to everything that is kept by where they stand.
    private static Matrix4x4 Placement(Mesh mesh, Matrix4x4 matrix, out int place)
    {
        int id = mesh.GetInstanceID();
        if (!placements.TryGetValue(id, out var known)) placements[id] = known = new List<Matrix4x4>();
        for (place = 0; place < known.Count; place++)
        {
            bool same = true;
            for (int i = 0; i < 16 && same; i++) same = Mathf.Abs(known[place][i] - matrix[i]) < 1e-4f;
            if (same) return known[place];
        }
        // A piece nudged again and again (the wardrobe's sliders) would file a place per nudge: start over.
        if (known.Count >= 16) known.Clear();
        place = known.Count;
        known.Add(matrix);
        return matrix;
    }

    // ---- Keeping ----

    // Puts the mesh on, its keys' weights as they were (a mesh made here has every key the one before it had, in order).
    private static void Wear(SkinnedMeshRenderer renderer, Mesh mesh)
    {
        var before = renderer.sharedMesh;
        if (before == mesh) return;
        int keys = before != null ? Mathf.Min(before.blendShapeCount, mesh.blendShapeCount) : 0;
        float[] weights = null;
        for (int key = 0; key < keys; key++)
        {
            float weight = renderer.GetBlendShapeWeight(key);
            if (weight == 0f) continue;
            weights ??= new float[keys];
            weights[key] = weight;
        }
        renderer.sharedMesh = mesh;
        if (weights == null) return;
        for (int key = 0; key < keys; key++)
            if (weights[key] != 0f) renderer.SetBlendShapeWeight(key, weights[key]);
    }

    private static void Keep<TKey, TValue>(Dictionary<TKey, TValue> kept, TKey key, TValue value, int most, Func<TValue, long> used)
    {
        if (kept.Count >= most) Shed(kept, most * 3 / 4, used, null);
        kept[key] = value;
    }

    // Down to the wanted number: first whatever is gone anyway, then the longest unused.
    private static void Shed<TKey, TValue>(Dictionary<TKey, TValue> kept, int wanted, Func<TValue, long> used, Func<TValue, bool> gone)
    {
        var order = new List<(long used, TKey key)>(kept.Count);
        foreach (var pair in kept) order.Add((gone != null && gone(pair.Value) ? long.MinValue : used(pair.Value), pair.Key));
        order.Sort((a, b) => a.used.CompareTo(b.used));
        for (int i = 0; i < order.Count && kept.Count > wanted; i++) kept.Remove(order[i].key);
    }

    // A made mesh goes only once no model wears it; with every kept one worn, the store grows instead.
    private static void KeepMesh<TKey>(Dictionary<TKey, Kept> kept, TKey key, Kept value)
    {
        long vertices = value.Vertices;
        foreach (var held in kept.Values) vertices += held.Vertices;
        if (kept.Count >= MostCopies || vertices > MostCopyVertices)
        {
            var order = new List<(long used, TKey key)>();
            foreach (var pair in kept)
                if (pair.Value.Wearers <= 0) order.Add((pair.Value.Used, pair.Key));
            order.Sort((a, b) => a.used.CompareTo(b.used));
            for (int i = 0; i < order.Count && (kept.Count > MostCopies * 3 / 4 || vertices > MostCopyVertices * 3 / 4); i++)
            {
                var old = kept[order[i].key];
                vertices -= old.Vertices;
                if (old.Mesh != null)
                {
                    drawnOf.Remove(old.Mesh.GetInstanceID());
                    Object.Destroy(old.Mesh);
                }
                kept.Remove(order[i].key);
            }
        }
        kept[key] = value;
    }

    private static void Warn(string message)
    {
        if (warned.Add(message)) Debug.LogWarning("[OutfitFit] " + message);
    }
}
