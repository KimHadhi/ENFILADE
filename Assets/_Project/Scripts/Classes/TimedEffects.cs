using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Every timed effect on this PC's own player, in one place: a class's own (a Point's Gun Kata, a Schießhund's Return
/// Back), a card's (Gunslinger, Momentum), and one a friend gave (an Assault's CHARGE, a Détente's adrenaline shot, a
/// Knight's cover). Each is an entry of its own with its own time left, and the HUD lays every one of them out in a slot
/// of its own (EffectStack), however many run at once.
/// <para>
/// The user, 2026-11-18: "건 카타 발동 중 무기를 교체해 공격력 증가 스킬이 추가 발동하는 경우 … 늦게 발동한 스킬의
/// 지속시간 하나만 남고 기존 정보가 덮어씌워지거나 … 독립적인 다중 슬롯/스택 레이아웃으로 분리 표시". Until then there was
/// no notion of a timed effect at all: each feature kept its own "until" and "total", and the class HUD had ONE bar,
/// filled by a hand-written chain of "this one if it runs, else that one" (a card's state first, so a gun swap's
/// Gunslinger hid the Gun Kata's time; a Knight's cover on a friend's guns was never shown). Every new effect meant
/// another branch in that chain, and whatever lost its place in it was simply not there.
/// </para>
/// <para>
/// How it works: the feature that owns an effect says so each frame the effect is on this PC's player (Report: which
/// one, how long is left of how long, its stacks, the number its name shows). Its clock stays where the effect's rule
/// is, so what the HUD shows can never drift from what the game applies, and there is nothing to start or stop and
/// forget: an effect nobody spoke for this frame is over. The entries keep the order they began in, so a slot never
/// jumps when another effect starts; the one above it ending lets the rest move up.
/// </para>
/// What each one is called, its colour and icon, and whether it shows at all are rows of Tables/TimedEffects (a mod
/// restyles or hides one there, and code of its own shows a new one by one Report with a new row).
/// Checked without playing: the console's /effects lists what runs, and every effect's beginning and end go to
/// Logs/timed-effects.txt with what else ran beside it (PlayRecord): the proof that two ran and showed at once.
/// </summary>
public static class TimedEffects
{
    public const string TableName = "TimedEffects", RecordName = "timed-effects";
    private const string RecordHeading = "every timed effect on this PC's player (TimedEffects): BEGIN id seconds | beside = what else ran then "
        + "(each in a slot of its own on the HUD); END id after seconds";

    /// <summary>What an effect is to the eye: a row of Tables/TimedEffects.</summary>
    public sealed class Kind
    {
        public string Id = "", Icon = "";
        public Color Colour = Color.white;
        public int Order;
        public bool Shown = true;
        internal string NameCell = "";

        /// <summary>Its name as the HUD writes it: {0} the number it was reported with (a share more, in %), {1} its stacks.</summary>
        public string Name(float value, int stacks)
        {
            string words = Texts.Cell(NameCell);
            if (words.IndexOf('{') < 0) return words;
            try { return string.Format(words, Mathf.RoundToInt(value), stacks); }
            catch (FormatException) { return words; }
        }
    }

    /// <summary>One effect running now.</summary>
    public struct Entry
    {
        public Kind Kind;
        /// <summary>Seconds left, and of how many.</summary>
        public float Left, Total;
        public int Stacks;
        /// <summary>The number its name shows (a share more, in %).</summary>
        public float Value;
        /// <summary>When it began (Time.time).</summary>
        public float Since;
        internal int Frame;

        /// <summary>How much of it is left, 0 to 1.</summary>
        public float Share => Total > 1e-4f ? Mathf.Clamp01(Left / Total) : 1f;
    }

    private static readonly Dictionary<string, Kind> kinds = new Dictionary<string, Kind>(StringComparer.OrdinalIgnoreCase);
    private static readonly List<Entry> running = new List<Entry>();
    private static bool loaded;
    private static int prunedAt = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        kinds.Clear();
        running.Clear();
        loaded = false;
        prunedAt = -1;
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    // The table saved while playing: read again at the next ask. What runs keeps running under the new rows.
    private static void Forget()
    {
        kinds.Clear();
        loaded = false;
        for (int i = 0; i < running.Count; i++)
        {
            var entry = running[i];
            entry.Kind = Find(entry.Kind.Id);
            running[i] = entry;
        }
    }

    /// <summary>An effect's row, by its id. One the table lacks still shows, under its id, so nothing reported is ever lost.</summary>
    public static Kind Find(string id)
    {
        Load();
        if (kinds.TryGetValue(id ?? "", out var kind)) return kind;
        kind = new Kind { Id = id ?? "", NameCell = id ?? "", Order = 1000 };
        kinds[kind.Id] = kind;
        return kind;
    }

    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (string.IsNullOrEmpty(id) || id[0] == '#' || !GameTables.TryGetRow(TableName, id, out var row)) continue;
            var kind = new Kind
            {
                Id = id.Trim(), NameCell = row.Text("name"), Icon = row.Text("icon").Trim(),
                Order = Mathf.RoundToInt(row.Number("order", kinds.Count)), Shown = row.Number("show", 1f) > .5f,
                Colour = ColourOf(row.Text("colour")),
            };
            kinds[kind.Id] = kind;
        }
    }

    // A colour cell: one of the HUD's own by name, or #RRGGBB. The HUD's accent and ice are its white emphasis and its red
    // the addon's (HudTone: the user's pick on 2026-11-22, the addon's colours only, no pink); green, cyan and the bounty's
    // teal stay, as effects told apart by them and the teal pairs with the bounty marks in the world.
    private static Color ColourOf(string cell)
    {
        switch ((cell ?? "").Trim().ToLowerInvariant())
        {
            case "accent": return HudTone.Text;
            case "ice": return HudTone.Text;
            case "red": return HudTone.Low;
            case "green": return KitStyle.Green;
            case "cyan": return KitStyle.Cyan;
            case "bounty": return PingMarks.BountyColor;
        }
        return ColorUtility.TryParseHtmlString(cell, out Color colour) ? colour : HudTone.Text;
    }

    /// <summary>
    /// The feature that owns an effect says, each frame it is on this PC's player: it runs, with <paramref name="left"/>
    /// seconds of <paramref name="total"/> to go, <paramref name="stacks"/> deep, <paramref name="value"/> the number its
    /// name shows. With nothing left it says nothing. Not said again the next frame, the effect is over.
    /// </summary>
    public static void Report(string id, float left, float total, int stacks = 0, float value = 0f)
    {
        if (left <= 0f || string.IsNullOrEmpty(id)) return;
        int frame = Time.frameCount;
        for (int i = 0; i < running.Count; i++)
        {
            var entry = running[i];
            if (!string.Equals(entry.Kind.Id, id, StringComparison.OrdinalIgnoreCase)) continue;
            // Made longer than it had left (a kill renewed it, a second dose): it runs on in its own slot.
            entry.Left = left;
            entry.Total = Mathf.Max(total, left);
            entry.Stacks = stacks;
            entry.Value = value;
            entry.Frame = frame;
            running[i] = entry;
            return;
        }
        var kind = Find(id);
        var begun = new Entry { Kind = kind, Left = left, Total = Mathf.Max(total, left), Stacks = stacks, Value = value, Since = Time.time, Frame = frame };
        // Among those that began this very frame, the table's order decides; an older one is never moved.
        int at = running.Count;
        while (at > 0 && running[at - 1].Since >= begun.Since && running[at - 1].Kind.Order > kind.Order) at--;
        running.Insert(at, begun);
        if (PlayRecord.On) PlayRecord.Line(RecordName, RecordHeading, $"BEGIN {id} {left:0.0}s of {begun.Total:0.0}s | beside {Beside(id)}");
    }

    /// <summary>
    /// The effects on this PC's player now, in the order they began (the HUD's slots, asked after every feature's
    /// Update). Those nobody spoke for this frame are over and leave here.
    /// </summary>
    public static IReadOnlyList<Entry> Running
    {
        get
        {
            Prune();
            return running;
        }
    }

    private static void Prune()
    {
        int frame = Time.frameCount;
        if (prunedAt == frame) return;
        prunedAt = frame;
        for (int i = running.Count - 1; i >= 0; i--)
        {
            // Spoken for last frame or this one: it runs (a feature whose Update comes after the asker's is a frame behind).
            if (running[i].Frame >= frame - 1) continue;
            if (PlayRecord.On) PlayRecord.Line(RecordName, RecordHeading, $"END   {running[i].Kind.Id} after {Time.time - running[i].Since:0.0}s");
            running.RemoveAt(i);
        }
    }

    // What else runs, as the record writes it.
    private static string Beside(string but)
    {
        var text = new StringBuilder();
        foreach (var entry in running)
        {
            if (string.Equals(entry.Kind.Id, but, StringComparison.OrdinalIgnoreCase)) continue;
            if (text.Length > 0) text.Append(", ");
            text.Append(entry.Kind.Id).Append(' ').Append(entry.Left.ToString("0.0")).Append('s');
        }
        return text.Length > 0 ? text.ToString() : "nothing";
    }

    /// <summary>The console's /effects: what runs on this PC's player now, each with its time and its slot's words.</summary>
    public static string Dump(string _)
    {
        Prune();
        if (running.Count == 0) return "지금 걸린 지속 효과가 없다"; // dev-text
        var text = new StringBuilder();
        text.Append("지속 효과 ").Append(running.Count).Append("개 (시작한 순서 = HUD 칸 순서)"); // dev-text
        foreach (var entry in running)
            text.Append('\n').Append(entry.Kind.Id).Append("  ").Append(entry.Kind.Name(entry.Value, entry.Stacks)).Append("  ")
                .Append(entry.Left.ToString("0.0")).Append(" / ").Append(entry.Total.ToString("0.0")).Append("s")
                .Append(entry.Kind.Shown ? "" : "  (표에서 숨김)"); // dev-text
        return text.ToString();
    }
}
