using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// The last word on skin drawn in under clothes (OutfitDresser calls it after everything else). A BOOTH body comes with
/// keys that pull its skin in where its clothes sit (Shrink_Knee_L under the stockings, Panty_Kuikomi under a band), set
/// on in its prefab because its maker never meant those clothes to come off, and CharacterParts.csv names the keys each
/// piece holds (needs). That list is written by hand, and twice a body was left hollow where it was wrong or never
/// asked: Mayo's shins with the stockings taken off (2026-11-11), then the see-through stockings of the wardrobe's 벗기
/// preview and her ankles left thin above her strap shoes, which the table said the shoes held (2026-11-12: "이거
/// 근본적으로 고쳐 줄 수 있어? … 벌써 두 번 째잖아").
/// So the meshes are measured, as made: for each such key still on (named Shrink or Kuikomi), how
/// much of the skin it moves has a garment drawn solid within reach (SkinCover.csv). A key the character came with on
/// keeps on while its skin is still about as covered as its maker had it (keep: Mayo's ankles were all under her
/// stockings, and only 85% near her strap shoes, which is the bare ankle above them); a key something else put on (an
/// outfit's fit) keeps on while nearly all of it is covered (share). Else it goes back to 0, whatever the table says or
/// a modder's avatar leaves out. Skin first, then garments' own keys (tights pulled in under shoes), leaving alone a
/// garment's key that the skin has on, which it follows. It only ever lets go, and the character as it came keeps every
/// key: the worst a wrong measure does is a little clipping, never a hollow. Measures are kept for the run, per
/// character. The numbers were checked on Mayo's and Ichigo's meshes before they went in (2026-11-12).
/// A key that shapes the body for a piece instead of drawing skin in (a heel, a stocking's band), held by the piece
/// (CharacterParts.csv needs) or put on by an item's fit (OutfitFits.csv), is the piece's: it holds while the piece
/// shows and goes with it, and coverage can't judge it. A heel moves the foot centimetres into the shape its shoe was
/// made around, so measured where the skin was made, the Sera Yura Mary-janes covered 55% of what Mayo's heel key moves
/// and her foot went flat through them on bare legs (2026-11-12). Her own shoes, high at the ankle, covered 99%.
/// </summary>
internal static class SkinCover
{
    public const string TableName = "SkinCover";
    // Pieces in these slots lie on the body without covering skin (hair, its ornaments, a tail).
    private static readonly HashSet<string> Bare = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hair", "hairpin", "tail" };

    private struct Values
    {
        public float Reach, Keep, Share, MaxMoved, MovedFrom;
    }

    /// <summary>What the character's own meshes show and have on as it came, read before dressing changes anything.</summary>
    internal sealed class Made
    {
        internal readonly HashSet<Renderer> Shown = new HashSet<Renderer>();
        internal readonly HashSet<(SkinnedMeshRenderer mesh, int key)> On = new HashSet<(SkinnedMeshRenderer, int)>();
    }

    /// <summary>Reads the character as it came: call on a fresh model, before anything is put on or shaped.</summary>
    internal static Made Read(Renderer[] own)
    {
        var made = new Made();
        if (own == null) return made;
        foreach (var renderer in own)
        {
            if (renderer == null) continue;
            if (renderer.enabled && renderer.gameObject.activeInHierarchy) made.Shown.Add(renderer);
            if (renderer is not SkinnedMeshRenderer mesh || mesh.sharedMesh == null) continue;
            for (int key = 0; key < mesh.sharedMesh.blendShapeCount; key++)
                if (mesh.GetBlendShapeWeight(key) > 0f) made.On.Add((mesh, key));
        }
        return made;
    }

    // The skin one key moves on one mesh, in the model's space as made.
    private sealed class Patch
    {
        public Vector3[] At;
        public Bounds Box;
    }

    // A garment's surface as its vertices in the model's space as made, filed in cells as wide as the reach. One whose
    // mesh can't be read is its box: it covers whatever lies inside.
    private sealed class Cloth
    {
        public Vector3[] At;
        public Dictionary<long, List<int>> Cells;
        public Bounds Box;
        public bool BoxOnly;
    }

    private static Values? values;
    private static bool listening;
    private static readonly Dictionary<(string skin, string renderer, int mesh, int key), Patch> patches =
        new Dictionary<(string, string, int, int), Patch>();
    private static readonly Dictionary<(string skin, string renderer, int mesh), Cloth> cloths = new Dictionary<(string, string, int), Cloth>();
    private static readonly Dictionary<(Patch, Cloth), BitArray> touched = new Dictionary<(Patch, Cloth), BitArray>();
    // How much of a key's skin the character's own clothes covered as it came.
    private static readonly Dictionary<Patch, float> asMade = new Dictionary<Patch, float>();
    private static readonly Dictionary<Mesh, Vector3[]> points = new Dictionary<Mesh, Vector3[]>();
    private static readonly HashSet<string> warned = new HashSet<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => Forget();

    private static void Forget()
    {
        values = null;
        patches.Clear();
        cloths.Clear();
        touched.Clear();
        asMade.Clear();
        points.Clear();
    }

    /// <summary>
    /// Lets go of the hide keys on the model's drawn meshes whose skin no solid garment covers any more. Returns those let
    /// go on the skin, for the worn clothes to follow.
    /// </summary>
    /// <param name="root">The character's model: positions are measured in its own space, so its fitted scale doesn't count.</param>
    /// <param name="own">The character's own renderers, before anything was put on.</param>
    /// <param name="items">The worn items' renderers drawn now.</param>
    /// <param name="ghosts">Drawn see-through (the wardrobe's leaving piece): they neither cover nor are measured.</param>
    /// <param name="made">The character as it came (Read), for how covered its keys were meant to be.</param>
    internal static HashSet<string> Release(Transform root, string skinId, Renderer[] own, List<Renderer> items, ICollection<Renderer> ghosts,
        Made made)
    {
        var released = new HashSet<string>(StringComparer.Ordinal);
        if (root == null || own == null) return released;
        if (!listening)
        {
            GameTables.Reloaded += Forget;
            listening = true;
        }
        var settings = Settings();
        var gone = new HashSet<Renderer>(ghosts ?? Array.Empty<Renderer>());
        // Which of its own meshes are its pieces, and which of them cover skin.
        var rows = OutfitCatalog.PartsOf(skinId);
        var pieces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clothing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            foreach (string name in OutfitCatalog.Split(row.parts))
            {
                pieces.Add(name);
                if (!Bare.Contains(row.slot)) clothing.Add(name);
            }
            foreach (string name in OutfitCatalog.Split(row.under))
            {
                pieces.Add(name);
                clothing.Add(name);
            }
        }
        // A character without rows: any other mesh of its own may cover, as it came.
        bool mapped = rows.Count > 0;
        made ??= new Made();
        var toModel = root.worldToLocalMatrix;
        var now = new List<(Renderer renderer, Cloth cloth)>();
        var then = new List<(Renderer renderer, Cloth cloth)>();
        foreach (var renderer in own)
        {
            if (renderer == null || (mapped && !clothing.Contains(renderer.name))) continue;
            bool solid = Solid(renderer, gone), shown = made.Shown.Contains(renderer);
            if (!solid && !shown) continue;
            var cloth = ClothOf(renderer, toModel, skinId, settings);
            if (solid) now.Add((renderer, cloth));
            if (shown) then.Add((renderer, cloth));
        }
        if (items != null)
            foreach (var renderer in items)
                if (Solid(renderer, gone)) now.Add((renderer, ClothOf(renderer, toModel, skinId, settings)));
        var skin = new List<SkinnedMeshRenderer>();
        var dressed = new List<SkinnedMeshRenderer>();
        foreach (var renderer in own)
            if (renderer is SkinnedMeshRenderer mesh && Solid(mesh, gone)) (mapped && pieces.Contains(mesh.name) ? dressed : skin).Add(mesh);
        if (items != null)
            foreach (var renderer in items)
                if (renderer is SkinnedMeshRenderer mesh && Solid(mesh, gone)) dressed.Add(mesh);
        var cover = new Cover { Now = now, Then = then, Made = made, ToModel = toModel, Skin = skinId, Settings = settings };
        var skinOn = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mesh in skin) Check(mesh, null, cover, released, skinOn);
        foreach (var mesh in dressed) Check(mesh, skinOn, cover, null, null);
        return released;
    }

    // What one Release measures against: the garments drawn solid now and those the character came in.
    private sealed class Cover
    {
        public List<(Renderer renderer, Cloth cloth)> Now, Then;
        public Made Made;
        public Matrix4x4 ToModel;
        public string Skin;
        public Values Settings;
    }

    private static bool Solid(Renderer renderer, HashSet<Renderer> gone) =>
        renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy && !gone.Contains(renderer);

    private static bool Hides(string key) =>
        key.IndexOf("shrink", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("kuikomi", StringComparison.OrdinalIgnoreCase) >= 0;

    // Every hide key on this mesh that is on: kept while garments other than this mesh cover its skin enough, else let go.
    // Enough is about as much as the character came with for a key it came with on, nearly all for one put on since. A
    // key that can't be measured (an unreadable mesh, one moving nothing or much of its mesh) is kept as it is, and so is
    // a key that shapes rather than hides (a heel): its piece holds it while it shows.
    private static void Check(SkinnedMeshRenderer mesh, HashSet<string> skip, Cover cover, HashSet<string> released, HashSet<string> stillOn)
    {
        var shared = mesh.sharedMesh;
        if (shared == null) return;
        for (int key = 0; key < shared.blendShapeCount; key++)
        {
            if (mesh.GetBlendShapeWeight(key) <= 0f) continue;
            string name = shared.GetBlendShapeName(key);
            if (!Hides(name)) continue;
            if (skip != null && skip.Contains(name)) continue;
            var patch = PatchOf(mesh, key, cover.ToModel, cover.Skin, cover.Settings);
            if (patch != null)
            {
                float needed = cover.Made.On.Contains((mesh, key))
                    ? AsMade(patch, mesh, cover) * cover.Settings.Keep
                    : cover.Settings.Share;
                if (Covered(patch, mesh, cover.Now, cover.Settings) < needed)
                {
                    mesh.SetBlendShapeWeight(key, 0f);
                    released?.Add(name);
                    continue;
                }
            }
            stillOn?.Add(name);
        }
    }

    // How much of the key's skin the character's own clothes covered as it came (kept for the run).
    private static float AsMade(Patch patch, Renderer self, Cover cover)
    {
        if (asMade.TryGetValue(patch, out float covered)) return covered;
        covered = Covered(patch, self, cover.Then, cover.Settings);
        asMade[patch] = covered;
        return covered;
    }

    // How much of the skin the garments cover between them (0 to 1).
    private static float Covered(Patch patch, Renderer self, List<(Renderer renderer, Cloth cloth)> garments, Values settings)
    {
        BitArray union = null;
        foreach (var (renderer, cloth) in garments)
        {
            if (renderer == self || cloth == null || !Near(patch.Box, cloth.Box, settings.Reach)) continue;
            var bits = Bits(patch, cloth, settings.Reach);
            if (union == null) union = new BitArray(bits);
            else union.Or(bits);
        }
        if (union == null) return 0f;
        int covered = 0;
        for (int i = 0; i < union.Length; i++)
            if (union[i]) covered++;
        return covered / (float)union.Length;
    }

    private static bool Near(Bounds patch, Bounds cloth, float reach)
    {
        cloth.Expand(reach * 2f);
        return patch.Intersects(cloth);
    }

    // Which of the skin's vertices have one of the garment's within reach (kept for the run).
    private static BitArray Bits(Patch patch, Cloth cloth, float reach)
    {
        if (touched.TryGetValue((patch, cloth), out var bits)) return bits;
        bits = new BitArray(patch.At.Length);
        if (cloth.BoxOnly)
        {
            var box = cloth.Box;
            box.Expand(reach * 2f);
            for (int i = 0; i < patch.At.Length; i++) bits[i] = box.Contains(patch.At[i]);
        }
        else
        {
            float reach2 = reach * reach;
            for (int i = 0; i < patch.At.Length; i++) bits[i] = Touches(cloth, patch.At[i], reach, reach2);
        }
        touched[(patch, cloth)] = bits;
        return bits;
    }

    private static bool Touches(Cloth cloth, Vector3 point, float reach, float reach2)
    {
        int x = Mathf.FloorToInt(point.x / reach), y = Mathf.FloorToInt(point.y / reach), z = Mathf.FloorToInt(point.z / reach);
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!cloth.Cells.TryGetValue(Cell(x + dx, y + dy, z + dz), out var near)) continue;
                    foreach (int index in near)
                        if ((cloth.At[index] - point).sqrMagnitude <= reach2) return true;
                }
        return false;
    }

    private static long Cell(int x, int y, int z) => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);

    // The model's space is the root's own, fitted scale and all taken out: the meshes as made, in the units they were
    // made in (metres for every model here, whose roots stand at scale 1 in their files), so the reach is in metres.
    private static Cloth ClothOf(Renderer renderer, Matrix4x4 toModel, string skinId, Values settings)
    {
        var mesh = MeshOf(renderer);
        if (mesh == null) return null;
        var id = (skinId ?? "", renderer.name, mesh.GetInstanceID());
        if (cloths.TryGetValue(id, out var cloth)) return cloth;
        var matrix = toModel * renderer.transform.localToWorldMatrix;
        cloth = new Cloth();
        if (!mesh.isReadable)
        {
            cloth.BoxOnly = true;
            cloth.Box = BoxOf(mesh.bounds, matrix);
            Warn($"{mesh.name}: 메시를 읽을 수 없어(Read/Write 꺼짐) 옷이 덮는 곳을 상자로만 잽니다.");
        }
        else
        {
            var made = PointsOf(mesh);
            cloth.At = new Vector3[made.Length];
            cloth.Cells = new Dictionary<long, List<int>>();
            for (int i = 0; i < made.Length; i++)
            {
                var at = matrix.MultiplyPoint3x4(made[i]);
                cloth.At[i] = at;
                if (i == 0) cloth.Box = new Bounds(at, Vector3.zero);
                else cloth.Box.Encapsulate(at);
                long cell = Cell(Mathf.FloorToInt(at.x / settings.Reach), Mathf.FloorToInt(at.y / settings.Reach), Mathf.FloorToInt(at.z / settings.Reach));
                if (!cloth.Cells.TryGetValue(cell, out var list)) cloth.Cells[cell] = list = new List<int>();
                list.Add(i);
            }
        }
        cloths[id] = cloth;
        return cloth;
    }

    // The vertices the key moves further than MovedFrom (the falloff round its region left out), as made.
    private static Patch PatchOf(SkinnedMeshRenderer renderer, int key, Matrix4x4 toModel, string skinId, Values settings)
    {
        var mesh = renderer.sharedMesh;
        var id = (skinId ?? "", renderer.name, mesh.GetInstanceID(), key);
        if (patches.TryGetValue(id, out var patch)) return patch;
        patch = null;
        int frames = mesh.GetBlendShapeFrameCount(key);
        if (!mesh.isReadable) Warn($"{mesh.name}: 메시를 읽을 수 없어(Read/Write 꺼짐) 옷 밑 피부 키를 그대로 둡니다.");
        else if (frames > 0)
        {
            var deltas = new Vector3[mesh.vertexCount];
            mesh.GetBlendShapeFrameVertices(key, frames - 1, deltas, null, null);
            var made = PointsOf(mesh);
            var matrix = toModel * renderer.transform.localToWorldMatrix;
            float from2 = settings.MovedFrom * settings.MovedFrom;
            var moved = new List<Vector3>();
            for (int i = 0; i < deltas.Length && i < made.Length; i++)
                if (matrix.MultiplyVector(deltas[i]).sqrMagnitude > from2) moved.Add(matrix.MultiplyPoint3x4(made[i]));
            // A key moving much of its mesh shapes the body (a heel lifts the whole foot on its own mesh, not much more):
            // it is no patch of skin to measure.
            if (moved.Count > 0 && moved.Count <= settings.MaxMoved * mesh.vertexCount)
            {
                patch = new Patch { At = moved.ToArray() };
                patch.Box = new Bounds(patch.At[0], Vector3.zero);
                foreach (var at in patch.At) patch.Box.Encapsulate(at);
            }
        }
        patches[id] = patch;
        return patch;
    }

    private static Vector3[] PointsOf(Mesh mesh)
    {
        if (!points.TryGetValue(mesh, out var made)) points[mesh] = made = mesh.vertices;
        return made;
    }

    private static Mesh MeshOf(Renderer renderer) =>
        renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
        : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;

    private static Bounds BoxOf(Bounds local, Matrix4x4 matrix)
    {
        var box = new Bounds(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
            box.Encapsulate(matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f))));
        return box;
    }

    private static Values Settings()
    {
        if (values.HasValue) return values.Value;
        var read = new Values { Reach = .03f, Keep = .9f, Share = .85f, MaxMoved = .3f, MovedFrom = .0005f };
        if (GameTables.TryGetRow(TableName, "Default", out var cells))
        {
            read.Reach = Mathf.Max(.002f, Number(cells, "reach", read.Reach));
            read.Keep = Mathf.Clamp01(Number(cells, "keep", read.Keep));
            read.Share = Mathf.Clamp01(Number(cells, "share", read.Share));
            read.MaxMoved = Mathf.Clamp01(Number(cells, "maxMoved", read.MaxMoved));
            read.MovedFrom = Mathf.Max(0f, Number(cells, "movedFrom", read.MovedFrom));
        }
        values = read;
        return read;
    }

    private static float Number(IReadOnlyDictionary<string, string> cells, string column, float fallback) =>
        cells.TryGetValue(column, out string text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value : fallback;

    private static void Warn(string message)
    {
        if (warned.Add(message)) Debug.LogWarning("[SkinCover] " + message);
    }
}
