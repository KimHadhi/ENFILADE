using System;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Multiplayer plan stage 6A: this PC's player's money for the run, the one number the player sees about it. Kills earn
/// points (CombatFeedHUD.TotalScore, unchanged) and every point is wonPerPoint of money (1: a kill is 100); it shows as a
/// plain number beside a banknote icon (MoneyIcon), no currency sign, never shortened (the user's picks: big sums,
/// ₩ and "만" read tacky). Money taken from or dropped for friends (MoneyDrops) and spent in shops (6B) move the balance,
/// never what was earned. Experience is not shown anywhere (money and experience side by side confused the user);
/// Experience below is kept only for class growth (6E), whose shape is still to settle. A run starts from nothing when
/// play starts, as the score does. Numbers: Tables/Money.
/// </summary>
public static class Wallet
{
    public const string TableName = "Money", TableRow = "Default";

    /// <summary>Won picked up from what friends dropped.</summary>
    public static long Received { get; private set; }
    /// <summary>Won dropped for friends.</summary>
    public static long Given { get; private set; }
    /// <summary>Won spent in shops (6B).</summary>
    public static long Spent { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRun() => Received = Given = Spent = 0;

    public static long WonPerPoint => Math.Max(1L, (long)Number("wonPerPoint", 1f));
    /// <summary>A sign before money numbers; empty by the user's pick, the money icon (MoneyIcon) stands beside them instead.</summary>
    public static string Symbol => Text("symbol", "");
    public static float LeftoverShare => Mathf.Clamp01(Number("leftoverShare", .5f));
    /// <summary>How much one drop for friends is (won).</summary>
    public static long DropAmount => Math.Max(1L, (long)Number("dropAmount", 500000f));
    /// <summary>How near a dropped bundle must be to pick it up (m).</summary>
    public static float PickupReach => Mathf.Max(.3f, Number("pickupReach", 1.3f));

    /// <summary>Everything kills have earned this run (won).</summary>
    public static long Earned => CombatFeedHUD.TotalScore * WonPerPoint;
    public static long Balance => Earned + Received - Given - Spent;

    /// <summary>Experience so far: every point earned, plus the share of the money held, in points.</summary>
    public static long Experience => CombatFeedHUD.TotalScore + LeftoverExperience;
    public static long LeftoverExperience => (long)(Math.Max(0L, Balance) / (double)WonPerPoint * LeftoverShare);

    /// <summary>Money as it reads: 1,240 (with the table's sign in front, if it has one). Never shortened.</summary>
    public static string Format(long won) => (won < 0 ? "-" : "") + Symbol + Math.Abs(won).ToString("N0", CultureInfo.InvariantCulture);
    public static string FormatPoints(long points) => Format(points * WonPerPoint);

    /// <summary>Takes <paramref name="amount"/> out to drop for friends, if there is that much.</summary>
    public static bool TryGive(long amount)
    {
        if (amount <= 0 || Balance < amount) return false;
        Given += amount;
        return true;
    }

    /// <summary>Puts back money that was taken out but never left (a drop the host refused).</summary>
    public static void Refund(long amount)
    {
        if (amount > 0) Given = Math.Max(0L, Given - amount);
    }

    public static void Receive(long amount)
    {
        if (amount > 0) Received += amount;
    }

    /// <summary>Pays for something (6B), if there is that much.</summary>
    public static bool TrySpend(long amount)
    {
        if (amount <= 0 || Balance < amount) return false;
        Spent += amount;
        return true;
    }

    /// <summary>Gives back a payment for something that then couldn't be handed over.</summary>
    public static void Unspend(long amount)
    {
        if (amount > 0) Spent = Math.Max(0L, Spent - amount);
    }

    private static float Number(string column, float fallback)
        => GameTables.TryGetRow(TableName, TableRow, out var row) && row.TryGetValue(column, out string text)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;

    private static string Text(string column, string fallback)
        => GameTables.TryGetRow(TableName, TableRow, out var row) && row.TryGetValue(column, out string text) && text.Trim().Length > 0
            ? text.Trim() : fallback;
}
