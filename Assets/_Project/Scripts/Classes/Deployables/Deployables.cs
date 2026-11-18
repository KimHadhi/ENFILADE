using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// What every player has standing in the world, in one place: how many of each kind, how many they may keep, and which
/// one goes when another is set down. Everything a player sets down and leaves (IPlaced: a Support's planks, wire,
/// ammo crate and searchlight, its claymore, and whatever a later class or a mod sets down) is entered here as it goes
/// up, on every PC, and is counted here and nowhere else.
/// <para>
/// The user, 2026-11-18: "탄약 박스, 바리케이드 등 추가 설치 시 기존 설치물이 소멸하는 기준을 파악할 수 있도록 현재 설치
/// 개수/최대 제한 현황 표시". Until then the count lived twice (Barricade.StandingOf and the Support's own most;
/// PlayerGrenades' list of mines and GrenadeRules claymoreMost), each with its own "the oldest goes" loop, the HUD
/// asked the first for the one kind in hand and hid even that while placing, and the mines had no count at all.
/// </para>
/// <para>
/// The kinds are rows of Tables/Deployables (a mod adds a row): a name, who holds it (a class, a card, a throwable
/// carried: all that are filled in must hold) and its most, a number or the name of a class number
/// ("Support.barricadeMax": ClassAbilities' cell, with the owner's cards on it as for every class number).
/// The rule is one: a player keeps at most that many of a kind; setting another down takes their oldest of that kind
/// away first (MakeRoom). It is the owner's own PC that counts, with its own cards, and tells the others which came
/// down (memory actor-decides); a copy on another PC never counts for itself.
/// </para>
/// The HUD's line (DeployHUD) and a preview's mark of the one that would go (Next) read the same numbers.
/// Checked without playing: the console's /deploy lists them, and every one put up or taken back for another leaves a
/// line in Logs/deployables.txt (PlayRecord).
/// </summary>
public static class Deployables
{
    public const string TableName = "Deployables", RecordName = "deployables";
    private const string RecordHeading = "what was set down and what came down for it (Deployables): UP kind count/most by owner | "
        + "RETIRED kind (its owner's oldest, past their most)";

    /// <summary>One kind of thing a player keeps standing: a row of Tables/Deployables.</summary>
    public sealed class Kind
    {
        public string Id = "";
        /// <summary>Who holds it: a class (a variant of it too), a card held, a throwable carried. Empty: no such condition.</summary>
        public string Class = "", Card = "", Throwable = "";
        public int Order;
        internal string NameCell = "", ShortCell = "", MostRow = "", MostName = "";
        internal float MostNumber = 1f;

        /// <summary>Its name (the placing prompt's), and the short one the HUD's count line has room for.</summary>
        public string Name => Texts.Cell(NameCell);
        public string Short => ShortCell.Length > 0 ? Texts.Cell(ShortCell) : Name;
    }

    /// <summary>A kind's standing on the HUD: how many of the owner's stand, the most, and whether they hold it now.</summary>
    public readonly struct Line
    {
        public readonly Kind Kind;
        public readonly int Count, Most;
        public readonly bool Held;

        public Line(Kind kind, int count, int most, bool held)
        {
            Kind = kind;
            Count = count;
            Most = most;
            Held = held;
        }

        /// <summary>As many stand as may: the next one set down takes the oldest away.</summary>
        public bool Full => Count >= Most;
    }

    private static readonly List<Kind> kinds = new List<Kind>();
    // Everything standing on this PC, anyone's, in the order it went up: the first of an owner's kind is their oldest.
    private static readonly List<IPlaced> standing = new List<IPlaced>();
    private static readonly Predicate<IPlaced> Gone = placed => placed == null || (placed is UnityEngine.Object thing && thing == null) || !placed.Stands;
    private static bool loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        kinds.Clear();
        standing.Clear();
        loaded = false;
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    // The table saved while playing: read again at the next ask (what stands stays).
    private static void Forget()
    {
        kinds.Clear();
        loaded = false;
    }

    // ---- The kinds ----

    /// <summary>Every kind in the table, in its order.</summary>
    public static IReadOnlyList<Kind> Kinds
    {
        get
        {
            Load();
            return kinds;
        }
    }

    /// <summary>A kind by its id, or null when the table has no such row.</summary>
    public static Kind Find(string id)
    {
        Load();
        foreach (var kind in kinds)
            if (string.Equals(kind.Id, id, StringComparison.OrdinalIgnoreCase)) return kind;
        return null;
    }

    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        kinds.Clear();
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (string.IsNullOrEmpty(id) || id[0] == '#' || !GameTables.TryGetRow(TableName, id, out var row)) continue;
            var kind = new Kind
            {
                Id = id.Trim().ToLowerInvariant(), NameCell = row.Text("name"), ShortCell = row.Text("short"),
                Class = row.Text("class").Trim(), Card = row.Text("card").Trim(), Throwable = row.Text("throwable").Trim(),
                Order = Mathf.RoundToInt(row.Number("order", kinds.Count)),
            };
            // Its most: a number of its own, or a class number by name (the owner's cards add to that one where it lives).
            string most = row.Text("most").Trim();
            int dot = most.IndexOf('.');
            if (TableCells.TryNumber(most, out float number)) kind.MostNumber = number;
            else if (dot > 0 && dot < most.Length - 1)
            {
                kind.MostRow = most.Substring(0, dot);
                kind.MostName = most.Substring(dot + 1);
            }
            kinds.Add(kind);
        }
        kinds.Sort((a, b) => a.Order.CompareTo(b.Order));
    }

    /// <summary>
    /// How many of a kind this PC's own player may keep standing: the table's, with their cards (a class number's are
    /// added where it lives: PlayerClass.Ability). Only the owner's PC asks (it decides which comes down).
    /// </summary>
    public static int Most(Kind kind)
    {
        if (kind == null) return 1;
        float most = kind.MostRow.Length > 0 ? PlayerClass.Ability(kind.MostRow, kind.MostName, kind.MostNumber) : kind.MostNumber;
        return Mathf.Max(1, Mathf.RoundToInt(most));
    }

    public static int Most(string kind) => Most(Find(kind));

    /// <summary>
    /// Whether this PC's own player may set this kind down now: every condition its row fills in holds (its class, a
    /// card, the throwable its class carries).
    /// </summary>
    public static bool Holds(Kind kind)
    {
        if (kind == null) return false;
        if (kind.Class.Length > 0 && !PlayerClass.Is(kind.Class)) return false;
        if (kind.Card.Length > 0 && !PlayerPerks.Has(kind.Card)) return false;
        if (kind.Throwable.Length > 0)
        {
            var carried = Throwables.Of(Throwables.ForClass(PlayerClass.CurrentId));
            if (carried == null || !string.Equals(carried.Id, kind.Throwable, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    public static bool Holds(string kind) => Holds(Find(kind));

    // ---- What stands ----

    /// <summary>
    /// Something was set down and stands, on this PC (its owner's own, or the copy of a friend's): told to whoever
    /// keeps things that lie loose with no body for physics to push out from under it. The field pickups listen: on
    /// the PC that keeps them, those the new structure's body holds make way (FieldPickups, Placement.Lie). Listeners
    /// subscribe once at load and outlive a play, as this class does.
    /// </summary>
    public static event Action<IPlaced> Stood;

    /// <summary>One went up, on this PC: its owner's own, or the copy of a friend's.</summary>
    public static void Add(IPlaced placed)
    {
        if (placed == null || standing.Contains(placed)) return;
        standing.RemoveAll(Gone);
        standing.Add(placed);
        var owner = placed.Owner;
        if (PlayRecord.On && owner != null && owner.IsLocal)
            PlayRecord.Line(RecordName, RecordHeading, $"UP      {placed.KindId} {Count(owner, placed.KindId)}/{Most(placed.KindId)} by {owner.name}");
        Stood?.Invoke(placed);
    }

    /// <summary>How many of this owner's of a kind stand now.</summary>
    public static int Count(PlayerMovement owner, string kind)
    {
        int count = 0;
        for (int i = 0; i < standing.Count; i++)
            if (Is(standing[i], owner, kind)) count++;
        return count;
    }

    /// <summary>This owner's of a kind (every kind when it is empty), oldest first.</summary>
    public static void Of(PlayerMovement owner, string kind, List<IPlaced> into)
    {
        into.Clear();
        for (int i = 0; i < standing.Count; i++)
            if (Is(standing[i], owner, kind)) into.Add(standing[i]);
    }

    private static bool Is(IPlaced placed, PlayerMovement owner, string kind)
        => !Gone(placed) && placed.Owner == owner && (string.IsNullOrEmpty(kind) || string.Equals(placed.KindId, kind, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The one of this PC's own player's that setting another of the kind down would take away: their oldest, once as
    /// many stand as they may keep. Null while there is room. A preview marks it (the planks' amber ghost, the mine's ring).
    /// </summary>
    public static IPlaced Next(PlayerMovement owner, string kind)
    {
        if (owner == null || Count(owner, kind) < Most(kind)) return null;
        for (int i = 0; i < standing.Count; i++)
            if (Is(standing[i], owner, kind)) return standing[i];
        return null;
    }

    /// <summary>
    /// On the owner's own PC, before another of the kind goes up: their oldest come down (Retire) until one fewer than
    /// they may keep stands. Those are added to <paramref name="retired"/>, for the others to be told.
    /// </summary>
    public static void MakeRoom(PlayerMovement owner, string kind, List<IPlaced> retired)
    {
        retired?.Clear();
        if (owner == null) return;
        int most = Most(kind), count = Count(owner, kind);
        for (int i = 0; i < standing.Count && count >= most; i++)
        {
            var oldest = standing[i];
            if (!Is(oldest, owner, kind)) continue;
            if (PlayRecord.On)
                PlayRecord.Line(RecordName, RecordHeading, $"RETIRED {oldest.KindId} of {owner.name}: {count} stood, most {most}"
                    + (oldest is Component thing ? " at " + PlayRecord.At(thing.transform.position) : ""));
            oldest.Retire();
            retired?.Add(oldest);
            count--;
        }
        standing.RemoveAll(Gone);
    }

    /// <summary>
    /// The HUD's lines for this PC's own player: every kind they hold now or still have standing (a class changed, a
    /// card gone), in the table's order.
    /// </summary>
    public static void Readout(PlayerMovement owner, List<Line> into)
    {
        into.Clear();
        if (owner == null) return;
        Load();
        foreach (var kind in kinds)
        {
            bool held = Holds(kind);
            int count = Count(owner, kind.Id);
            if (held || count > 0) into.Add(new Line(kind, count, Most(kind), held));
        }
    }

    // ---- The console's /deploy ----

    /// <summary>What this PC's own player holds and has standing, and everything standing here, as words.</summary>
    public static string Report(string _)
    {
        var mine = PlayerRoster.Local;
        var text = new StringBuilder();
        var lines = new List<Line>();
        Readout(mine, lines);
        text.Append("내 설치물 (").Append(lines.Count).Append("종류)"); // dev-text
        foreach (var line in lines)
        {
            text.Append('\n').Append(line.Kind.Id).Append("  ").Append(line.Kind.Name).Append(' ').Append(line.Count).Append('/').Append(line.Most);
            if (!line.Held) text.Append("  (지금은 못 놓음)"); // dev-text
            if (Next(mine, line.Kind.Id) is Component next) text.Append("  다음에 사라질 것: ").Append(PlayRecord.At(next.transform.position)); // dev-text
        }
        standing.RemoveAll(Gone);
        text.Append("\n이 PC에 서 있는 것 모두: ").Append(standing.Count); // dev-text
        foreach (var placed in standing)
            text.Append('\n').Append(placed.KindId).Append("  ").Append(placed.Owner != null ? placed.Owner.name : "?")
                .Append(placed is Component thing ? "  " + PlayRecord.At(thing.transform.position) : "");
        return text.ToString();
    }
}
