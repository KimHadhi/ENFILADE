using System.Globalization;
using UnityEngine;

/// <summary>
/// The 2026-11-11 stealth round's rewards ("리턴 확실하게"): a takedown of a zombie that never knew they were there
/// (암살, on top of the execution's points: CombatFeedHUD.Kill), and a quiet stretch got through to the rest stop, or to
/// where a hold begins, without one zombie finding anyone (every player is paid: CampaignRun). Points are money in
/// play (Wallet). Numbers: Stealth.csv Score.
/// </summary>
public static class StealthScore
{
    public const string TableName = "Stealth", TableRow = "Score";

    /// <summary>Points on top of an execution's for a takedown of one that never knew they were there.</summary>
    public static int Takedown => Number("takedown", 50);

    /// <summary>Points to every player for a quiet stretch got through unseen.</summary>
    public static int Unseen => Number("unseen", 300);

    /// <summary>
    /// Points for killing one that made someone out before it could call the others (ZombieMind's tell: kill the witness
    /// and nobody else is told).</summary>
    public static int Witness => Number("witness", 30);

    private static int Number(string column, int fallback)
        => GameTables.TryGetRow(TableName, TableRow, out var row) && row.TryGetValue(column, out string text)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? Mathf.Max(0, Mathf.RoundToInt(value)) : fallback;
}
