using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// A character's own palms on a gun through a reload (FPSBody.Contacts): KINEMATION's clip puts the middle of each palm
/// where its own hand had it, and a thicker or longer hand may want it a little back from the gun or along the fingers.
/// This is the user's idea of a fine offset per character (2026-11-12: "세부 조정 오프셋"), kept as the last touch now
/// that the contact points carry the clip itself.
/// Rows: Tables/GunHands.csv, id = the character's skin id (PlayerBodyProfile.Skin.Id: kazusa-band, hiro, sil, mayo,
/// ichigo), or "default" for any character without a row of its own (a mod's character adds a row with its id). Columns
/// rightBack, rightAlong, leftBack, leftAlong in metres: back is against the way the palm faces (away from the gun),
/// along is the way the fingers point. Edited in Play, Tools > Data Tables > Reload Tables applies it at once.
/// </summary>
public static class GunHands
{
    public const string TableName = "GunHands";

    private struct Offsets
    {
        public float RightBack, RightAlong, LeftBack, LeftAlong;
    }

    private static Dictionary<string, Offsets> table;
    private static bool listening;

    /// <summary>The palm <paramref name="palm"/> moved by the character <paramref name="skin"/>'s offsets for that hand.</summary>
    internal static Vector3 Palm(string skin, bool isLeft, Vector3 palm, Vector3 fingers, Vector3 facing)
    {
        var rows = Table();
        if (!rows.TryGetValue(skin ?? "", out var offsets) && !rows.TryGetValue("default", out offsets)) return palm;
        float back = isLeft ? offsets.LeftBack : offsets.RightBack, along = isLeft ? offsets.LeftAlong : offsets.RightAlong;
        return palm - facing.normalized * back + fingers.normalized * along;
    }

    private static Dictionary<string, Offsets> Table()
    {
        if (table != null) return table;
        if (!listening)
        {
            listening = true;
            GameTables.Reloaded += () => table = null;
        }
        table = new Dictionary<string, Offsets>(System.StringComparer.OrdinalIgnoreCase);
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (!GameTables.TryGetRow(TableName, id, out var row)) continue;
            table[id] = new Offsets
            {
                RightBack = Number(row, "rightBack"),
                RightAlong = Number(row, "rightAlong"),
                LeftBack = Number(row, "leftBack"),
                LeftAlong = Number(row, "leftAlong")
            };
        }
        return table;
    }

    private static float Number(IReadOnlyDictionary<string, string> row, string column)
        => row.TryGetValue(column, out string text) && !string.IsNullOrWhiteSpace(text)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0f;
}
