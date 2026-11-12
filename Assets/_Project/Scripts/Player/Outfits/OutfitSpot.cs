using UnityEngine;

/// <summary>
/// Where a piece made for no one in particular hangs on a character: a row of OutfitSpots.csv, id "slot@skin"
/// ("ears@mayo"). A BOOTH accessory sold for any avatar (the Ene earrings, 2026-11-12) comes as a bare prefab with bones
/// of its own, fitted to nobody's skeleton, and VRChat players drag it onto the ear by hand; nothing in it says where this
/// character's lobes are. So the spot is measured once per character from its meshes, from the slot's bone along the
/// model's own axes (x right, y up, z forward, metres): the right side's, the left being its mirror. The model's axes
/// rather than the bone's, because every rig turns its head bone its own way while a humanoid always stands facing +Z.
/// A piece is offered only to characters whose spot for its slot is known (OutfitCatalog.Model): Sil has no ears.
/// </summary>
public sealed class OutfitSpot
{
    public const string TableName = "OutfitSpots";

    /// <summary>The right side's spot from the slot's bone, "x|y|z" in metres along the model's right, up and forward.</summary>
    public Vector3 at;
    /// <summary>
    /// How far the piece's front is turned forward from facing straight out to the side (degrees). Ear lobes face out
    /// and forward (33-52° measured on Mayo, Kazusa and Hiro), and a dangle hung square to the side shows only its edge
    /// from the front.
    /// </summary>
    public float turn;

    /// <summary>Whether the character has this slot's spot measured.</summary>
    public static bool Has(string slotId, string skinId) => GameTables.TryGetRow(TableName, Key(slotId, skinId), out _);

    /// <summary>The slot's spot on the character, or null when it has none.</summary>
    public static OutfitSpot Of(string slotId, string skinId)
    {
        if (!GameTables.TryGetRow(TableName, Key(slotId, skinId), out var cells)) return null;
        var spot = new OutfitSpot();
        foreach (var cell in cells)
            if (TableFieldWriter.TryWrite(spot, cell.Key, cell.Value, out string error) == TableWrite.Invalid)
                Debug.LogWarning($"[Outfits] {TableName}.csv [{Key(slotId, skinId)}] '{cell.Key}' = '{cell.Value}': {error}.");
        return spot;
    }

    private static string Key(string slotId, string skinId) => (slotId ?? "") + OutfitLook.Separator + (skinId ?? "");

    /// <summary>
    /// Hangs a freshly placed model on the spots and gives it to the slot's bone to follow. Its root's children named
    /// "… R" and "… L" (a pair of earrings) go on the right and the left spot; a model without them is one piece on the
    /// right. A piece's origin is the point that sits on the spot (where an earring goes through the lobe) and its front
    /// faces +Z as made; it is turned outward and then <see cref="turn"/> back toward the front, mirrored on the left.
    /// The model stands as it was made, so the bone is where it rests; the pieces are placed in the model's own frame,
    /// which the body's fit scale already sized.
    /// </summary>
    internal bool Hang(Transform instance, Transform model, Transform bone)
    {
        if (instance == null || model == null || bone == null) return false;
        var from = model.InverseTransformPoint(bone.position);
        bool sided = false;
        for (int i = 0; i < instance.childCount; i++)
        {
            var piece = instance.GetChild(i);
            int side = Side(piece.name);
            if (side == 0) continue;
            Put(piece, model, from, side);
            sided = true;
        }
        if (!sided) Put(instance, model, from, 1);
        instance.SetParent(bone, true);
        return true;
    }

    private void Put(Transform piece, Transform model, Vector3 from, int side)
    {
        piece.position = model.TransformPoint(from + new Vector3(at.x * side, at.y, at.z));
        piece.rotation = model.rotation * Quaternion.Euler(0f, side * (90f - turn), 0f);
    }

    // 1 for a right piece ("Earring1 R", "Bow_R", "Ribbon.R"), -1 for a left one, 0 for neither.
    private static int Side(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length < 2) return 0;
        char gap = name[name.Length - 2], mark = char.ToUpperInvariant(name[name.Length - 1]);
        if (gap != ' ' && gap != '_' && gap != '.') return 0;
        return mark == 'R' ? 1 : mark == 'L' ? -1 : 0;
    }
}
