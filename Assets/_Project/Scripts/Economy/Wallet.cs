using System;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Multiplayer plan stage 6A: this PC's player's money for the run, the one number the player sees about it. Kills earn
/// points (CombatFeedHUD.TotalScore, unchanged) and the points turn into money; it shows as a plain number beside a
/// banknote icon (MoneyIcon), no currency sign, never shortened (the user's picks: big sums, ₩ and "만" read tacky).
/// Money taken from or dropped for friends (MoneyDrops) and spent in shops (6B) move the balance, never what was earned.
/// Experience is not shown anywhere (money and experience side by side confused the user); Experience below is kept
/// only for class growth (6E). A run starts from nothing when play starts, as the score does. Numbers: Tables/Money.
/// <para>
/// 2026-11-11, the user: money "rots" (records: a chapter's escape is some 170 kills, 72% headshots, and the shop's
/// dearest thing cost 2000), so supply less. A point is now worth earnShare of wonPerPoint, a share that grows with the
/// team: the director sends as many zombies for one player as for four, so a team splits the kills, and each player
/// should earn about the same whatever the team's size (alone a plain kill pays 40, four together 130). Each point is
/// turned into money at the share for the team as it was when it came in (Sync), so someone joining or leaving changes
/// only what comes after. Back at the title screen the score starts again from 0, and so does the wallet (before, what
/// was spent stayed and the next run could start below nothing).
/// </para>
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

    // What kills have paid so far (won, kept exact: a share of a point can be a fraction), and the score it counts to.
    private static double earned;
    private static long seenScore;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRun()
    {
        Received = Given = Spent = 0;
        earned = 0d;
        seenScore = 0;
    }

    public static long WonPerPoint => Math.Max(1L, (long)Number("wonPerPoint", 1f));
    /// <summary>A sign before money numbers; empty by the user's pick, the money icon (MoneyIcon) stands beside them instead.</summary>
    public static string Symbol => Text("symbol", "");
    public static float LeftoverShare => Mathf.Clamp01(Number("leftoverShare", .5f));
    /// <summary>How much one drop for friends is (won).</summary>
    public static long DropAmount => Math.Max(1L, (long)Number("dropAmount", 500000f));
    /// <summary>How near a dropped bundle must be to pick it up (m).</summary>
    public static float PickupReach => Mathf.Max(.3f, Number("pickupReach", 1.3f));

    /// <summary>
    /// The share of a point that is money now, for the team's size (Tables/Money earnShare: one value per player count,
    /// "1;2;3;4" players, the last one for more).
    /// </summary>
    public static float EarnShare
    {
        get
        {
            string list = Text("earnShare", "1");
            var shares = list.Split(new[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (shares.Length == 0) return 1f;
            int players = Mathf.Max(1, PlayerRoster.All.Count);
            string pick = shares[Mathf.Clamp(players - 1, 0, shares.Length - 1)];
            return TableCells.TryNumber(pick, out float share) ? Mathf.Max(0f, share) : 1f;
        }
    }

    /// <summary>Everything kills have earned this run (won).</summary>
    public static long Earned
    {
        get
        {
            Sync();
            return (long)Math.Floor(earned + 1e-6);
        }
    }

    public static long Balance => Earned + Received - Given - Spent;

    /// <summary>Experience so far: every point earned, plus the share of the money held, in points.</summary>
    public static long Experience => CombatFeedHUD.TotalScore + LeftoverExperience;
    public static long LeftoverExperience => (long)(Math.Max(0L, Balance) / (double)WonPerPoint * LeftoverShare);

    /// <summary>Money as it reads: 1,240 (with the table's sign in front, if it has one). Never shortened.</summary>
    public static string Format(long won) => (won < 0 ? "-" : "") + Symbol + Math.Abs(won).ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>What <paramref name="points"/> come to in money now (the kill popup's +60).</summary>
    public static long FromPoints(long points) => (long)Math.Round(points * (double)WonPerPoint * EarnShare);

    public static string FormatPoints(long points) => Format(FromPoints(points));

    /// <summary>Takes <paramref name="amount"/> out to drop for friends, if there is that much.</summary>
    public static bool TryGive(long amount)
    {
        if (amount <= 0 || Balance < amount) return false;
        Given += amount;
        return true;
    }

    public static void Receive(long amount)
    {
        Sync();
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

    // The points that came in since the last look become money at the share for the team now. The score going back
    // down means a new run (the title screen sets it to 0, MainMenuScreen): the wallet starts again with it.
    private static void Sync()
    {
        long score = CombatFeedHUD.TotalScore;
        if (score < seenScore || (MainMenuScreen.IsOpen && score == 0 && (earned > 0d || Received != 0 || Given != 0 || Spent != 0)))
            ResetRun();
        if (score == seenScore) return;
        earned += (score - seenScore) * (double)WonPerPoint * EarnShare;
        seenScore = score;
    }

    private static float Number(string column, float fallback)
        => GameTables.Number(TableName, TableRow, column, fallback);

    private static string Text(string column, string fallback)
        => GameTables.Text(TableName, TableRow, column, fallback);
}
