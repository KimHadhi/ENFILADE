using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Underwear goes on the skin, under whatever else is worn (the underwear slots, OutfitSlot.under), and what is worn over
/// it decides how much of it shows. It is measured on the meshes on any character, so a modder's avatar or outfit needs no
/// rows for it (as SkinCover measures skin):
/// - Covered (UnderwearFit.csv hide: that share of it lies within SkinCover's reach of what is over it): out of sight.
///   Mayo's dress covers all of her bra and panties, the Sailor Maiden jacket 98.7% of the bra, Ichigo's costume with its
///   outer 97% of her underwear top.
/// - Partly covered (a short skirt, an open jacket, a cropped top): it shows and goes under the rest. First its own fit
///   keys, named Fit, Shrink, Kuikomi, x or OFF (Ichigo's Underwear_Costume_Fit, the Sera Yura bikini's Shrink_Bell,
///   Mayo's panties' Ribbon_OFF), go on where what they move is covered (keyShare): made to tuck the piece under
///   clothes, they change only what the clothes hide. Then whatever still stands out through a garment, or lies less
///   than margin under it, is pressed in under it on a copy of its mesh (Press).
/// What the meshes showed (2026-11-12): the maker kept Mayo's bra and panties off under her dress, where they stood up to
/// 23 mm out through it; a tight dress or a chest belt over the bra left it up to 4 mm out, and her panties' ribbon stood
/// 26 mm out through a skirt's band. Her bra has no fit keys at all, so the press is what keeps it under.
/// </summary>
internal static class UnderwearFit
{
    public const string TableName = "UnderwearFit";

    private struct Values
    {
        public float Hide, KeyShare, Margin, Press, Facing, Cell;
    }

    // A key named with one of these words tucks its piece under clothes: Underwear_Costume_Fit, Shrink_Bell_L,
    // Panty_Kuikomi, Underwear_Ribbon_chest_x, Ribbon_OFF.
    private static readonly HashSet<string> FitWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "fit", "shrink", "kuikomi", "x", "off", "hide" };
    private static readonly char[] KeyBreaks = { '_', ' ', '-', '.' };
    // A fit key doesn't go on where nearly all it moves is moved by a fit key on already (OutfitDresser.Stacks).
    private const float StackShare = .9f;
    // Split copies of one point (a seam) are one point to the press: their places agree to a hundredth of a millimetre.
    private const float WeldScale = 1e5f;
    // Presses under a tenth of a millimetre leave the mesh as it is.
    private const float LeastMove = 1e-4f;

    private static Values? values;
    private static bool listening;
    private static readonly Dictionary<(Mesh, int), bool[]> movedBy = new Dictionary<(Mesh, int), bool[]>();
    private static readonly HashSet<string> warned = new HashSet<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => Forget();

    private static void Forget()
    {
        values = null;
        movedBy.Clear();
    }

    private static void Listen()
    {
        if (listening) return;
        GameTables.Reloaded += Forget;
        listening = true;
    }

    /// <summary>
    /// Hides the underwear the rest covers and puts its fit keys on where what they move is covered. Call once everything
    /// is on and shaped, before SkinCover: underwear left showing covers skin too. Returns the underwear left showing.
    /// </summary>
    /// <param name="root">The character's model: measured in its own space, as SkinCover measures.</param>
    /// <param name="own">The character's own renderers: a key its body has is the body's, followed rather than fitted.</param>
    /// <param name="under">Each underwear piece on (an item, or the character's own in its slot), its renderers together.</param>
    /// <param name="over">Everything else drawn solid that covers the body: not hair or a tail, nothing on its way off.</param>
    internal static List<Renderer> Fit(Transform root, string skinId, Renderer[] own, List<List<Renderer>> under, List<Renderer> over)
    {
        var showing = new List<Renderer>();
        if (root == null || under == null) return showing;
        Listen();
        var settings = Settings();
        HashSet<string> body = null;
        foreach (var piece in under)
        {
            // A piece is judged whole: a bikini's strings don't go while its cups show.
            if (over != null && over.Count > 0 && Covers(root, skinId, piece, over) >= settings.Hide)
            {
                foreach (var renderer in piece) renderer.enabled = false;
                continue;
            }
            showing.AddRange(piece);
            if (over == null || over.Count == 0) continue;
            body ??= BodyKeys(own, skinId);
            foreach (var renderer in piece)
                if (renderer is SkinnedMeshRenderer mesh) FitKeys(root, skinId, mesh, over, body, settings);
        }
        return showing;
    }

    // How much of the piece the garments cover, its renderers weighed by their vertices; -1 when none can be measured.
    private static float Covers(Transform root, string skinId, List<Renderer> piece, List<Renderer> over)
    {
        float covered = 0f, all = 0f;
        foreach (var renderer in piece)
        {
            float share = SkinCover.Covers(root, skinId, renderer, over);
            var mesh = MeshOf(renderer);
            if (share < 0f || mesh == null) continue;
            covered += share * mesh.vertexCount;
            all += mesh.vertexCount;
        }
        return all > 0f ? covered / all : -1f;
    }

    // The piece's fit keys go on where the clothes over it cover what they move. A key its body has too (Breast_Big, or
    // Panty_Kuikomi on the skin under the band) is the body's, followed as dressing set it. One fit key at a time on a
    // stretch of the piece, as OutfitDresser.Stacks keeps draw-in keys: Ichigo's Underwear_Costume_Fit and
    // Underwear_Costume_Fit_2 both tuck her cups, and both on would move them twice as far.
    private static void FitKeys(Transform root, string skinId, SkinnedMeshRenderer piece, List<Renderer> over, HashSet<string> body, Values settings)
    {
        var mesh = piece.sharedMesh;
        if (mesh == null || !mesh.isReadable) return;
        var on = new List<bool[]>();
        for (int key = 0; key < mesh.blendShapeCount; key++)
            if (piece.GetBlendShapeWeight(key) > 0f && IsFitKey(mesh.GetBlendShapeName(key)) && Moved(mesh, key) is bool[] moved) on.Add(moved);
        for (int key = 0; key < mesh.blendShapeCount; key++)
        {
            string name = mesh.GetBlendShapeName(key);
            if (piece.GetBlendShapeWeight(key) > 0f || !IsFitKey(name) || body.Contains(name)) continue;
            if (Moved(mesh, key) is not bool[] moved || Stacks(moved, on)) continue;
            if (SkinCover.CoversKey(root, skinId, piece, key, over) < settings.KeyShare) continue;
            piece.SetBlendShapeWeight(key, 100f);
            on.Add(moved);
        }
    }

    /// <summary>Whether a key tucks its piece under clothes: named with the word Fit, Shrink, Kuikomi, x, OFF or Hide.</summary>
    internal static bool IsFitKey(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (string word in name.Split(KeyBreaks, StringSplitOptions.RemoveEmptyEntries))
            if (FitWords.Contains(word)) return true;
        return false;
    }

    private static bool Stacks(bool[] mine, List<bool[]> on)
    {
        if (on.Count == 0) return false;
        int moved = 0, taken = 0;
        for (int i = 0; i < mine.Length; i++)
        {
            if (!mine[i]) continue;
            moved++;
            foreach (var other in on)
                if (i < other.Length && other[i])
                {
                    taken++;
                    break;
                }
        }
        return moved > 0 && taken >= moved * StackShare;
    }

    // Which of a mesh's vertices a key moves (over a millimetre), read once per mesh and key; null for none or unreadable.
    private static bool[] Moved(Mesh mesh, int key)
    {
        if (movedBy.TryGetValue((mesh, key), out var known)) return known;
        bool[] moved = null;
        int frames = mesh.isReadable ? mesh.GetBlendShapeFrameCount(key) : 0;
        if (frames > 0)
        {
            var deltas = new Vector3[mesh.vertexCount];
            mesh.GetBlendShapeFrameVertices(key, frames - 1, deltas, null, null);
            moved = new bool[deltas.Length];
            bool any = false;
            for (int i = 0; i < deltas.Length; i++)
            {
                moved[i] = deltas[i].sqrMagnitude > 1e-6f;
                any |= moved[i];
            }
            if (!any) moved = null;
        }
        return movedBy[(mesh, key)] = moved;
    }

    // The keys of the character's own meshes that are no piece of its outfit: its body's, its face's.
    private static HashSet<string> BodyKeys(Renderer[] own, string skinId)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (own == null) return keys;
        var pieces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in OutfitCatalog.PartsOf(skinId)) pieces.UnionWith(OutfitCatalog.Split(row.parts));
        foreach (var renderer in own)
        {
            if (renderer is not SkinnedMeshRenderer skinned || skinned.sharedMesh == null || pieces.Contains(renderer.name)) continue;
            var mesh = skinned.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++) keys.Add(mesh.GetBlendShapeName(i));
        }
        return keys;
    }

    /// <summary>
    /// Presses in whatever of the showing underwear still stands out through a garment over it, or lies less than margin
    /// under one, on copies of its meshes the model keeps (OutfitMeshes). Each point goes in along its normal until the
    /// first garment surface over it facing out toward it is margin above it, at most margin + press: pressed parts lie
    /// under clothes, so nothing that shows changes, and where it comes out from under a garment's edge it tucks under
    /// it. Call last, with every key where dressing leaves it.
    /// </summary>
    internal static void Press(Transform root, List<Renderer> showing, List<Renderer> over)
    {
        if (root == null || showing == null || showing.Count == 0 || over == null || over.Count == 0) return;
        var settings = Settings();
        if (settings.Margin + settings.Press <= 0f) return;
        var toModel = root.worldToLocalMatrix;
        var pieces = new List<(SkinnedMeshRenderer Renderer, Mesh Mesh, Matrix4x4 Matrix, Vector3[] At)>();
        var near = new Bounds();
        bool started = false;
        foreach (var renderer in showing)
        {
            if (renderer is not SkinnedMeshRenderer piece || !renderer.enabled || piece.sharedMesh == null) continue;
            var mesh = piece.sharedMesh;
            if (!mesh.isReadable)
            {
                Warn($"{mesh.name}: 메시를 읽을 수 없어(Read/Write 꺼짐) 옷 밖으로 나온 속옷을 눌러 넣지 못합니다.");
                continue;
            }
            var matrix = toModel * piece.transform.localToWorldMatrix;
            var at = Posed(piece, mesh, matrix);
            if (at.Length == 0) continue;
            foreach (var point in at)
            {
                if (started) near.Encapsulate(point);
                else near = new Bounds(point, Vector3.zero);
                started = true;
            }
            pieces.Add((piece, mesh, matrix, at));
        }
        if (pieces.Count == 0) return;
        near.Expand(2f * (settings.Margin + settings.Press));
        var shell = Shell.Of(over, toModel, near, settings);
        if (shell == null) return;
        foreach (var (piece, mesh, matrix, at) in pieces) PressIn(root, piece, mesh, matrix, at, shell, settings);
    }

    private static void PressIn(Transform root, SkinnedMeshRenderer piece, Mesh mesh, Matrix4x4 matrix, Vector3[] at, Shell shell, Values settings)
    {
        var normals = mesh.normals;
        if (normals.Length != at.Length) return;
        // Split copies of one point (a seam) move as one, along their mean normal, so no seam opens.
        var weld = new Dictionary<Vector3Int, int>();
        var group = new int[at.Length];
        var spots = new List<Vector3>();
        var ways = new List<Vector3>();
        for (int i = 0; i < at.Length; i++)
        {
            var key = Vector3Int.RoundToInt(at[i] * WeldScale);
            if (!weld.TryGetValue(key, out int spot))
            {
                weld[key] = spot = spots.Count;
                spots.Add(at[i]);
                ways.Add(Vector3.zero);
            }
            group[i] = spot;
            ways[spot] += matrix.MultiplyVector(normals[i]).normalized;
        }
        var moves = new Vector3[spots.Count];
        bool any = false;
        for (int spot = 0; spot < spots.Count; spot++)
        {
            Vector3 way = ways[spot];
            if (way.sqrMagnitude < 1e-12f) continue;
            way.Normalize();
            float depth = shell.Depth(spots[spot], way, settings);
            if (depth < LeastMove) continue;
            moves[spot] = -way * depth;
            any = true;
        }
        if (!any) return;
        // The press goes onto the mesh as made: its keys still move it from there as they did.
        var made = mesh.vertices;
        var back = matrix.inverse;
        for (int i = 0; i < made.Length; i++)
        {
            Vector3 move = moves[group[i]];
            if (move.sqrMagnitude > 0f) made[i] += back.MultiplyVector(move);
        }
        var copy = Object.Instantiate(mesh);
        copy.name = mesh.name;
        copy.vertices = made;
        piece.sharedMesh = copy;
        OutfitMeshes.Keep(root.gameObject, copy);
    }

    // The garments over the underwear as triangles, posed as dressing left them, in the model's space, filed in cells.
    private sealed class Shell
    {
        private readonly List<Vector3> at = new List<Vector3>(), facing = new List<Vector3>();
        private readonly List<int> corners = new List<int>();
        private readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
        // A triangle over this many cells (a huge flat one) is left out: no garment over underwear is made of those.
        private const int MostCells = 4096;
        private float size;
        private int[] seen;
        private int mark;

        // Only what lies near the underwear; null when nothing does.
        internal static Shell Of(List<Renderer> over, Matrix4x4 toModel, Bounds near, Values settings)
        {
            var shell = new Shell { size = settings.Cell };
            var done = new HashSet<Renderer>();
            var triangles = new List<int>();
            foreach (var renderer in over)
            {
                if (renderer == null || !done.Add(renderer)) continue;
                var mesh = MeshOf(renderer);
                if (mesh == null) continue;
                var matrix = toModel * renderer.transform.localToWorldMatrix;
                if (!near.Intersects(MeshTools.BoxOf(mesh.bounds, matrix))) continue;
                if (!mesh.isReadable)
                {
                    Warn($"{mesh.name}: 메시를 읽을 수 없어(Read/Write 꺼짐) 그 밑의 속옷을 눌러 넣지 못합니다.");
                    continue;
                }
                var points = Posed(renderer, mesh, matrix);
                var normals = mesh.normals;
                // Without normals there is no telling its outside from its inside.
                if (normals.Length != points.Length) continue;
                int first = shell.at.Count;
                shell.at.AddRange(points);
                foreach (var normal in normals) shell.facing.Add(matrix.MultiplyVector(normal).normalized);
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                    mesh.GetTriangles(triangles, sub);
                    for (int t = 0; t + 2 < triangles.Count; t += 3)
                        shell.File(first + triangles[t], first + triangles[t + 1], first + triangles[t + 2], near);
                }
            }
            if (shell.corners.Count == 0) return null;
            shell.seen = new int[shell.corners.Count / 3];
            return shell;
        }

        private void File(int a, int b, int c, Bounds near)
        {
            Vector3 pa = at[a], pb = at[b], pc = at[c];
            Vector3 min = Vector3.Min(pa, Vector3.Min(pb, pc)), max = Vector3.Max(pa, Vector3.Max(pb, pc));
            Vector3 low = near.min, high = near.max;
            if (max.x < low.x || max.y < low.y || max.z < low.z || min.x > high.x || min.y > high.y || min.z > high.z) return;
            int x0 = Floor(min.x), y0 = Floor(min.y), z0 = Floor(min.z), x1 = Floor(max.x), y1 = Floor(max.y), z1 = Floor(max.z);
            if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1) > MostCells) return;
            int triangle = corners.Count / 3;
            corners.Add(a);
            corners.Add(b);
            corners.Add(c);
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                    {
                        long cell = SkinCover.Cell(x, y, z);
                        if (!cells.TryGetValue(cell, out var list)) cells[cell] = list = new List<int>();
                        list.Add(triangle);
                    }
        }

        // How far to press a point in along -way so the first garment surface over it facing out toward it sits margin
        // above it: from margin above the point down to that surface, within margin + press; 0 when there is none.
        internal float Depth(Vector3 point, Vector3 way, Values settings)
        {
            float reach = settings.Margin + settings.Press;
            Vector3 from = point + way * settings.Margin, down = -way, to = from + down * reach;
            Vector3 min = Vector3.Min(from, to), max = Vector3.Max(from, to);
            int x0 = Floor(min.x), y0 = Floor(min.y), z0 = Floor(min.z), x1 = Floor(max.x), y1 = Floor(max.y), z1 = Floor(max.z);
            mark++;
            float best = float.MaxValue;
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                    {
                        if (!cells.TryGetValue(SkinCover.Cell(x, y, z), out var list)) continue;
                        foreach (int triangle in list)
                        {
                            if (seen[triangle] == mark) continue;
                            seen[triangle] = mark;
                            int a = corners[triangle * 3], b = corners[triangle * 3 + 1], c = corners[triangle * 3 + 2];
                            float distance = Hit(from, down, at[a], at[b], at[c], out float u, out float v);
                            if (distance <= 0f || distance > reach || distance >= best) continue;
                            Vector3 outward = facing[a] * (1f - u - v) + facing[b] * u + facing[c] * v;
                            // Only a surface facing out toward the point lies over it. A garment's inside, met from under
                            // it, has the point properly in; one met edge-on is the turn of a fold or a hem.
                            if (Vector3.Dot(outward, down) > -settings.Facing * outward.magnitude) continue;
                            best = distance;
                        }
                    }
            return best == float.MaxValue ? 0f : best;
        }

        private int Floor(float value) => Mathf.FloorToInt(value / size);
    }

    // Where a ray meets a triangle (Möller-Trumbore): its distance along the ray, and the triangle's weights there; -1 for none.
    private const float EdgeSlack = 1e-4f;

    private static float Hit(Vector3 from, Vector3 ray, Vector3 a, Vector3 b, Vector3 c, out float u, out float v)
    {
        u = v = 0f;
        Vector3 ab = b - a, ac = c - a, p = Vector3.Cross(ray, ac);
        float det = Vector3.Dot(ab, p);
        if (Mathf.Abs(det) < 1e-12f) return -1f;
        float inverse = 1f / det;
        Vector3 s = from - a;
        u = Vector3.Dot(s, p) * inverse;
        if (u < -EdgeSlack || u > 1f + EdgeSlack) return -1f;
        Vector3 q = Vector3.Cross(s, ab);
        v = Vector3.Dot(ray, q) * inverse;
        if (v < -EdgeSlack || u + v > 1f + EdgeSlack) return -1f;
        return Vector3.Dot(ac, q) * inverse;
    }

    // The mesh's vertices with its keys as they are now, in the model's space (FaceFit reads faces with it too).
    internal static Vector3[] Posed(Renderer renderer, Mesh mesh, Matrix4x4 matrix)
    {
        var at = mesh.vertices;
        if (renderer is SkinnedMeshRenderer skinned)
        {
            Vector3[] deltas = null;
            for (int key = 0; key < mesh.blendShapeCount; key++)
            {
                float weight = skinned.GetBlendShapeWeight(key);
                int frame = mesh.GetBlendShapeFrameCount(key) - 1;
                if (weight == 0f || frame < 0) continue;
                float full = mesh.GetBlendShapeFrameWeight(key, frame);
                if (full == 0f) continue;
                deltas ??= new Vector3[at.Length];
                mesh.GetBlendShapeFrameVertices(key, frame, deltas, null, null);
                float share = weight / full;
                for (int i = 0; i < at.Length; i++) at[i] += deltas[i] * share;
            }
        }
        for (int i = 0; i < at.Length; i++) at[i] = matrix.MultiplyPoint3x4(at[i]);
        return at;
    }

    private static Mesh MeshOf(Renderer renderer) =>
        renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
        : renderer != null && renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;

    private static Values Settings()
    {
        if (values.HasValue) return values.Value;
        var read = new Values { Hide = .9f, KeyShare = .85f, Margin = .003f, Press = .02f, Facing = .3f, Cell = .025f };
        if (GameTables.TryGetRow(TableName, "Default", out var cells))
        {
            read.Hide = Mathf.Clamp01(cells.Number("hide", read.Hide));
            read.KeyShare = Mathf.Clamp01(cells.Number("keyShare", read.KeyShare));
            read.Margin = Mathf.Max(0f, cells.Number("margin", read.Margin));
            read.Press = Mathf.Max(0f, cells.Number("press", read.Press));
            read.Facing = Mathf.Clamp01(cells.Number("facing", read.Facing));
            read.Cell = Mathf.Max(.005f, cells.Number("cell", read.Cell));
        }
        values = read;
        return read;
    }

    private static void Warn(string message)
    {
        if (warned.Add(message)) Debug.LogWarning("[Underwear] " + message);
    }
}
