using UnityEngine;

/// <summary>
/// A spot someone measured by hand on one character, for one way of wearing a piece: a row of OutfitSpots.csv, id
/// "position@skin" ("lobes@mayo"). A BOOTH accessory sold for any avatar (the Ene earrings, 2026-11-12) comes as a bare
/// prefab with bones of its own, fitted to nobody's skeleton, and VRChat players drag it onto the ear by hand; nothing
/// in it says where this character's lobes are. The position's own rule puts it at a spot given from the eyes in eye
/// spacings (WearPosition "spot"), which lands near the lobe on any head with eye bones but not on it: finding the lobe
/// itself from a head's meshes was tried and does not hold (2026-11-18, on the game's five bodies: within 8 mm on Mayo
/// and 5 mm on Ichigo, 5 cm off on Kazusa, whose face hangs from bones of its own; on Sil the hair counted as the
/// head; the mannequin has no eye bones; and the hair's outside is 5 cm from the lobe on every anime head). So a
/// character somebody measured keeps that measure here, and it wins over the rule: the piece's spot from the slot's
/// bone along the way the body faces (x right, y up, z ahead, metres of the model), the right side's, the left being its
/// mirror. A piece's origin is the point that sits on the spot (where an earring goes through the lobe) and its front
/// faces +Z as made; it is turned outward and then <see cref="turn"/> back toward the front (OutfitDresser.AtSpot).
/// A row is a correction, never a condition: a character with none wears the piece by the rule (before 2026-11-18 it
/// was not offered the piece at all), and the player moves it from there (ItemAdjust).
/// </summary>
public sealed class OutfitSpot
{
    public const string TableName = "OutfitSpots";

    /// <summary>The right side's spot from the slot's bone, "x|y|z" in metres along the body's right, up and ahead.</summary>
    public Vector3 at;
    /// <summary>
    /// How far the piece's front is turned forward from facing straight out to the side (degrees). Ear lobes face out
    /// and forward (33-52° measured on Mayo, Kazusa and Hiro), and a dangle hung square to the side shows only its edge
    /// from the front.
    /// </summary>
    public float turn;

    /// <summary>The hand-measured spot of a position on the character, or null when nobody measured it.</summary>
    public static OutfitSpot Of(string positionId, string skinId)
    {
        if (string.IsNullOrEmpty(positionId) || !GameTables.TryGetRow(TableName, Key(positionId, skinId), out var cells)) return null;
        var spot = new OutfitSpot();
        foreach (var cell in cells)
            if (TableFieldWriter.TryWrite(spot, cell.Key, cell.Value, out string error) == TableWrite.Invalid)
                Debug.LogWarning($"[Outfits] {TableName}.csv [{Key(positionId, skinId)}] '{cell.Key}' = '{cell.Value}': {error}.");
        return spot;
    }

    private static string Key(string positionId, string skinId) => (positionId ?? "") + OutfitLook.Separator + (skinId ?? "");

    /// <summary>Which of a pair a part is, by its name: 1 for a right piece ("Earring1 R", "Bow_R", "Ribbon.R"), -1 for a left one, 0 for neither.</summary>
    internal static int SideOf(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length < 2) return 0;
        char gap = name[name.Length - 2], mark = char.ToUpperInvariant(name[name.Length - 1]);
        if (gap != ' ' && gap != '_' && gap != '.') return 0;
        return mark == 'R' ? 1 : mark == 'L' ? -1 : 0;
    }
}
