using System;
using System.Collections.Generic;
using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>One of a player's hands, or both.</summary>
[Flags]
public enum HandSide : byte
{
    None = 0,
    Left = 1,
    Right = 2,
    Both = Left | Right,
}

/// <summary>What becomes of the gun while something else has the right hand (HandUses.csv, gun).</summary>
public enum GunFate : byte
{
    /// <summary>The right hand leaves the gun: the gun stays with whatever hand it has left, and with none it is put away.</summary>
    Leaves,
    /// <summary>The gun is put away for as long as the use lasts (an execution's knife, a climb).</summary>
    Away,
    /// <summary>The right hand stays on the gun and nothing of the gun's can be done (a zombie's grip, a guard break).</summary>
    Stays,
}

/// <summary>
/// One thing a hand is used for: a row of Tables/Player/HandUses.csv. Which hands it must have (Needs), which it takes
/// too when nobody stronger wants them (Also), how strong its claim is (Rank), in which states of the body its arm can
/// be up, what becomes of the gun when it takes the right hand, whether nothing else of the hands may start while
/// it has them, and whether only some families of uses may be asked for while it stands (Only). A use decides
/// nothing: HandBoard reads these and says whose each hand is.
/// </summary>
public sealed class HandUse
{
    /// <summary>The rank of a row that has none: it only asks (the trigger: "is the right hand the gun's?") and never holds.</summary>
    public const int NoRank = -1;
    /// <summary>Every state of the body: the stances of a row that says "any".</summary>
    public const ulong AnyStance = ulong.MaxValue;

    private readonly ulong stances;
    private readonly string[] only;

    public HandUse(string id, HandSide needs, HandSide also, int rank, GunFate gun = GunFate.Leaves, bool locks = false, ulong stances = AnyStance,
        string[] only = null)
    {
        Id = id ?? "";
        int dot = Id.IndexOf('.');
        Family = dot > 0 ? Id.Substring(0, dot) : Id;
        this.only = only != null && only.Length > 0 ? only : null;
        Needs = needs;
        // A hand it must have is not also one it takes only when free.
        Also = also & ~needs;
        Rank = rank;
        Gun = gun;
        Locks = locks;
        this.stances = stances;
    }

    public string Id { get; }
    /// <summary>The family it belongs to: its id up to the first dot (gun.reload is the gun's).</summary>
    public string Family { get; }
    /// <summary>The hands it must have: without all of them it does not start, and losing one ends it.</summary>
    public HandSide Needs { get; }
    /// <summary>The hands it takes too while nobody stronger wants them: given up, it goes on (the left hand under an aimed gun).</summary>
    public HandSide Also { get; }
    /// <summary>How strong its claim is: the bigger takes a hand from the smaller, and of two alike the one there first keeps it.</summary>
    public int Rank { get; }
    public GunFate Gun { get; }
    /// <summary>While it has its hands nothing else of the hands may start (PlayerMovement.CombatInputLocked).</summary>
    public bool Locks { get; }

    /// <summary>
    /// While it stands only uses of the families it names may be asked for (HandUses.csv only): it takes no hand for
    /// that, the hands stay where they are and can do nothing else. A body gone down is so: its hands are on its pistol,
    /// whatever the pistol does goes on (a reload too), and nothing else of the hands can be had. False: it names none.
    /// </summary>
    public bool Limits => only != null;

    /// <summary>The families it lets be asked for, as the table writes them; empty when it stops nothing.</summary>
    public string Only => only != null ? string.Join("|", only) : "";

    /// <summary>Whether <paramref name="other"/> may be asked for while this stands.</summary>
    public bool Lets(HandUse other)
    {
        if (only == null || other == null) return true;
        for (int i = 0; i < only.Length; i++)
            if (string.Equals(only[i], other.Family, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Its arm can be up in this state of the body. Outside it the hand is still its own: what it holds waits.</summary>
    public bool StandsIn(MovementState state) => (stances & (1UL << (int)state)) != 0;

    public override string ToString() => Id;
}

/// <summary>
/// The uses of a player's hands, as Tables/Player/HandUses.csv has them: the list a mod adds a row to. Code names a use
/// by its id (the constants here are the game's own) and asks HandOwner for it; nothing in the game works out for
/// itself whether a hand is free (the user, 2026-11-18: "이런 왼손 오른손 기능 따로 쓰려고 하는데 구조 설계 잘 돼
/// 있죠?", and, told that it was only half so, "문제들 다 근본적으로 고칠 수 있게 시작해요"). The design is
/// Docs/Features/Hands.md.
/// </summary>
public static class HandUses
{
    public const string TableName = "HandUses";

    // ---- The game's own rows ----
    // What the body or a feature is doing already: stated, never asked for.
    public const string Dead = "life.dead", Downed = "life.downed", ZombieHold = "zombie.hold", Execution = "melee.execution",
        Sheath = "melee.sheath", GuardBreak = "melee.guardbreak", Counter = "melee.counter", Bandage = "detente.bandage",
        Climb = "move.climb", Vault = "move.vault", Roll = "move.roll", Can = "carry.can";
    // Something held in the hand, or a gesture of it.
    public const string Grenade = "grenade.throw", Dose = "detente.dose", Repair = "support.repair", GuardPose = "guard.pose",
        PointPing = "point.ping", PointHold = "point.hold", CalloutArm = "callout.arm";
    // The whole body showing a feeling (the callout wheel's 감정 page: PlayerBody.Emote), the gun put away meanwhile.
    public const string Emote = "body.emote";
    // What the gun does with the hands that are on it.
    public const string GunHold = "gun.hold", GunAim = "gun.aim", GunFire = "gun.fire", GunReload = "gun.reload",
        GunInspect = "gun.inspect", GunSwap = "gun.swap", GunBash = "gun.bash";

    private static readonly Dictionary<string, HandUse> rows = new Dictionary<string, HandUse>(StringComparer.OrdinalIgnoreCase);
    private static readonly List<HandUse> listed = new List<HandUse>();
    private static readonly HashSet<string> unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static bool loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Forget();
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    /// <summary>The table was saved while playing (or a check wants it afresh): read again at the next ask.</summary>
    public static void Forget()
    {
        loaded = false;
        rows.Clear();
        listed.Clear();
        unknown.Clear();
    }

    /// <summary>The row of this id; null when the table has none (said once in the log: the use then never has a hand).</summary>
    public static HandUse Of(string id)
    {
        Load();
        if (id != null && rows.TryGetValue(id, out var use)) return use;
        if (unknown.Add(id ?? ""))
            Debug.LogWarning($"[HandUses] '{id}' has no row in Tables/Player/{TableName}.csv: it never gets a hand until one is written.");
        return null;
    }

    /// <summary>Whether the table has this row, asked quietly.</summary>
    public static bool Has(string id)
    {
        Load();
        return id != null && rows.ContainsKey(id);
    }

    /// <summary>Every row, in the table's own order.</summary>
    public static IReadOnlyList<HandUse> All
    {
        get
        {
            Load();
            return listed;
        }
    }

    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        rows.Clear();
        listed.Clear();
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (string.IsNullOrEmpty(id) || id[0] == '#' || !GameTables.TryGetRow(TableName, id, out var row)) continue;
            var use = Read(id.Trim(), row, null);
            rows[use.Id] = use;
            listed.Add(use);
        }
        if (listed.Count == 0)
            Debug.LogError($"[HandUses] Tables/Player/{TableName}.csv has no rows: nothing can be given a hand.");
    }

    /// <summary>
    /// A row as the game reads it. <paramref name="problems"/> gets a line for every cell that says nothing the game
    /// knows (the editor's check lists them); the game itself reads such a cell as empty.
    /// </summary>
    internal static HandUse Read(string id, IReadOnlyDictionary<string, string> row, List<string> problems)
    {
        HandSide needs = Side(row.Text("hands"), id, "hands", problems), also = Side(row.Text("also"), id, "also", problems);
        string rankText = row.Text("rank");
        int rank = HandUse.NoRank;
        if (rankText.Length > 0 && !TableCells.TryWhole(rankText, out rank))
        {
            rank = HandUse.NoRank;
            problems?.Add($"{id}: rank '{rankText}' is not a whole number");
        }
        string gunText = row.Text("gun").ToLowerInvariant();
        GunFate gun = gunText == "away" ? GunFate.Away : gunText == "stays" ? GunFate.Stays : GunFate.Leaves;
        if (gunText.Length > 0 && gun == GunFate.Leaves) problems?.Add($"{id}: gun '{gunText}' is neither away nor stays");
        var only = new List<string>();
        foreach (string family in TableCells.Parts(row.Text("only"))) only.Add(family);
        if (needs == HandSide.None && also == HandSide.None && only.Count == 0)
            problems?.Add($"{id}: it names no hand and limits nothing (hands, also and only are all empty)");
        return new HandUse(id, needs, also, rank, gun, row.Flag("locks", false), Stances(row.Text("stances"), id, problems), only.ToArray());
    }

    private static HandSide Side(string text, string id, string cell, List<string> problems)
    {
        switch (text.ToLowerInvariant())
        {
            case "": return HandSide.None;
            case "left": return HandSide.Left;
            case "right": return HandSide.Right;
            case "both": return HandSide.Both;
            default:
                problems?.Add($"{id}: {cell} '{text}' is not left, right or both");
                return HandSide.None;
        }
    }

    // The states its arm can be up in: the names of PlayerMovement.MovementState with | between them; "any" (or nothing) is all of them.
    private static ulong Stances(string text, string id, List<string> problems)
    {
        if (text.Length == 0 || string.Equals(text, "any", StringComparison.OrdinalIgnoreCase)) return HandUse.AnyStance;
        ulong mask = 0;
        foreach (string name in TableCells.Parts(text))
        {
            if (Enum.TryParse(name, true, out MovementState state) && Enum.IsDefined(typeof(MovementState), state)) mask |= 1UL << (int)state;
            else problems?.Add($"{id}: stances names '{name}', which is no state of the body (PlayerMovement.MovementState)");
        }
        return mask;
    }
}
