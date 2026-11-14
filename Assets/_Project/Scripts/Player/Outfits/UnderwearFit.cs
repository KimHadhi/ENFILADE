using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Underwear goes on the skin, under whatever else is worn (the underwear slots, OutfitSlot.under), and what is worn over
/// it decides how much of it shows. It is measured on the meshes on any character, so a modder's avatar or outfit needs no
/// rows for it (as SkinCover measures skin):
/// - Covered (UnderwearFit.csv hide: that share of it lies within SkinCover's reach of what is over it): out of sight.
///   Mayo's dress covers all of her bra and panties, the Sailor Maiden jacket 98.7% of the bra, Ichigo's costume with its
///   outer 97% of her underwear top.
/// - Partly covered (a short skirt, an open jacket, a cropped top): it shows. Its own fit keys, named Fit, Shrink,
///   Kuikomi, x or OFF (Ichigo's Underwear_Costume_Fit, the Sera Yura bikini's Shrink_Bell, Mayo's panties' Ribbon_OFF),
///   go on where what they move is covered (keyShare): made to tuck the piece under clothes, they change only what the
///   clothes hide. What of it then still lies under the clothes, or stands out through them, is left out of its mesh
///   with every other layer (OutfitDresser.FitLayers, OutfitFit.Trim).
/// What the meshes showed (2026-11-12): the maker kept Mayo's bra and panties off under her dress, where they stood up to
/// 23 mm out through it; a tight dress or a chest belt over the bra left it up to 4 mm out, and her panties' ribbon stood
/// 26 mm out through a skirt's band. Her bra has no fit keys at all.
/// It used to press what stood out in under the clothes along each point's normal; the same press under shoes crushed
/// feet (the user, 2026-11-14), and the fit that took its place there takes it here too.
/// </summary>
internal static class UnderwearFit
{
    public const string TableName = "UnderwearFit";

    private struct Values
    {
        public float Hide, KeyShare;
    }

    // A key named with one of these words tucks its piece under clothes: Underwear_Costume_Fit, Shrink_Bell_L,
    // Panty_Kuikomi, Underwear_Ribbon_chest_x, Ribbon_OFF.
    private static readonly HashSet<string> FitWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "fit", "shrink", "kuikomi", "x", "off", "hide" };
    private static readonly char[] KeyBreaks = { '_', ' ', '-', '.' };
    // A fit key doesn't go on where nearly all it moves is moved by a fit key on already (OutfitDresser.Stacks).
    private const float StackShare = .9f;

    private static Values? values;
    private static bool listening;
    private static readonly Dictionary<(Mesh, int), bool[]> movedBy = new Dictionary<(Mesh, int), bool[]>();

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
            var mesh = MeshTools.MeshOf(renderer);
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

    // The mesh's vertices with its keys as they are now, in the model's space (FaceFit reads faces with it).
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

    private static Values Settings()
    {
        if (values.HasValue) return values.Value;
        var read = new Values { Hide = .9f, KeyShare = .85f };
        if (GameTables.TryGetRow(TableName, "Default", out var cells))
        {
            read.Hide = Mathf.Clamp01(cells.Number("hide", read.Hide));
            read.KeyShare = Mathf.Clamp01(cells.Number("keyShare", read.KeyShare));
        }
        values = read;
        return read;
    }
}
