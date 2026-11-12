using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// The kill feed's flairs (the user, 2026-11-12: "킬로그 무기 아이콘 쪽에 이펙트도 있었음 좋겠어 … 사람들이 킬로그 올라가는
/// 것만 봐도 와 멋지다 간지난다 할 것 같아"): what a new row plays for how its kill happened. Each row of
/// Resources/Tables/KillFlairs.csv names the conditions it needs (<see cref="KillMarks"/>, all of them; or a run of kills in a
/// row), how much it matters against the others, what plays on the weapon's icon and on the row, its colour, its words and
/// its timing. A kill takes the icon's effect from the most important row that has one and the row's effects likewise, so
/// at most two rows speak for one kill (a charged Schießhund's bounty: fire on the gun and the contract's stamp). A row
/// with on = 0 stays in the table and plays nothing. KillFlairFx plays them on the row (KillFeedRow).
/// </summary>
public static class KillFlairs
{
    public const string TableName = "KillFlairs";

    /// <summary>What plays on the weapon's icon (KillFlairFx): the shapes of KillFlair.shader, or pieces round the icon.</summary>
    public enum Icon { None, Fire, Gild, Trail, Tint }

    /// <summary>What plays on the row round the icon, any of them together.</summary>
    [Flags]
    public enum Row
    {
        None = 0,
        Edge = 1 << 0,    // the plate's edge lines in the flair's colour (KillRowPlate.Highlight)
        Sheen = 1 << 1,   // a band of the colour crossing the plate (KillRowPlate.Sheen)
        Stamp = 1 << 2,   // the words on a stamp slammed on above the row's end
        Strike = 1 << 3,  // a line through the one killed
        Money = 1 << 4,   // what the kill paid on top, under the row's end (a Schießhund's bounty)
        Frame = 1 << 5,   // corner brackets snapping in round the one killed
        Tick = 1 << 6,    // a check drawn through the one killed
        Seal = 1 << 7,    // a round seal with the words, on the row's end
        Chip = 1 << 8,    // the words on a small plate under the row's end
        Kick = 1 << 9,    // the row jolts as the flair lands
        Tag = 1 << 10,    // the words, small, over the weapon's icon
        Name = 1 << 11,   // the one killed turns the flair's colour
    }

    /// <summary>One row of the table.</summary>
    public sealed class Flair
    {
        public string Id;
        public KillMarks When;
        public int Multi, Priority;
        public bool On;
        public Icon Icon;
        public Row Row;
        public Color Colour = Color.white;
        public string Text = "";
        public float Delay, Seconds = 1f;
    }

    /// <summary>What one kill's row plays: the icon's effect and the row's, each from its own table row (or none).</summary>
    public readonly struct Play
    {
        public readonly Flair Icon, Row;
        public readonly KillMarks Marks;
        public Play(Flair icon, Flair row, KillMarks marks) { Icon = icon; Row = row; Marks = marks; }
        public bool Any => Icon != null || Row != null;
    }

    private static List<Flair> flairs;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        flairs = null;
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    private static void Forget() => flairs = null;

    /// <summary>
    /// What a kill under <paramref name="marks"/> plays, <paramref name="run"/> being its place in the killer's run of quick
    /// kills (1 for a lone one, 2 for the second within the streak window).
    /// </summary>
    public static Play Pick(KillMarks marks, int run)
    {
        Flair icon = null, row = null;
        foreach (var flair in All())
        {
            if (!flair.On || (flair.When == KillMarks.None && flair.Multi <= 0)) continue;
            if ((marks & flair.When) != flair.When || (flair.Multi > 0 && run < flair.Multi)) continue;
            if (icon == null && flair.Icon != Icon.None) icon = flair;
            if (row == null && flair.Row != Row.None) row = flair;
            if (icon != null && row != null) break;
        }
        return new Play(icon, row, marks);
    }

    /// <summary>Every row, the most important first.</summary>
    public static IReadOnlyList<Flair> All()
    {
        if (flairs != null) return flairs;
        flairs = new List<Flair>();
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (!GameTables.TryGetRow(TableName, id, out var cells)) continue;
            var flair = new Flair { Id = id };
            if (cells.TryGetValue("when", out string when))
                foreach (string part in when.Split(';', ',', ' ', '+'))
                    if (part.Length > 0 && Enum.TryParse(part.Trim(), true, out KillMarks mark)) flair.When |= mark;
                    else if (part.Trim().Length > 0) Debug.LogWarning($"[KillFlairs] {TableName}.csv [{id}] when: '{part}' is not a KillMarks name.");
            flair.Multi = Whole(cells, "multi", 0);
            flair.Priority = Whole(cells, "priority", 0);
            flair.On = Whole(cells, "on", 1) != 0;
            if (cells.TryGetValue("icon", out string icon) && !Enum.TryParse(icon.Trim(), true, out flair.Icon))
                Debug.LogWarning($"[KillFlairs] {TableName}.csv [{id}] icon: '{icon}' is not one of {string.Join(", ", Enum.GetNames(typeof(Icon)))}.");
            if (cells.TryGetValue("row", out string rowText))
                foreach (string part in rowText.Split(';', ',', ' ', '+'))
                    if (part.Length > 0 && Enum.TryParse(part.Trim(), true, out Row piece)) flair.Row |= piece;
                    else if (part.Trim().Length > 0) Debug.LogWarning($"[KillFlairs] {TableName}.csv [{id}] row: '{part}' is not one of {string.Join(", ", Enum.GetNames(typeof(Row)))}.");
            if (cells.TryGetValue("colour", out string colour) && ColorUtility.TryParseHtmlString(colour.Trim(), out var parsed)) flair.Colour = parsed;
            if (cells.TryGetValue("text", out string text)) flair.Text = text.Trim();
            flair.Delay = Number(cells, "delay", 0f);
            flair.Seconds = Mathf.Max(.1f, Number(cells, "seconds", 1f));
            flairs.Add(flair);
        }
        flairs.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        return flairs;
    }

    private static int Whole(IReadOnlyDictionary<string, string> cells, string field, int fallback)
        => cells.TryGetValue(field, out string raw) && int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;

    private static float Number(IReadOnlyDictionary<string, string> cells, string field, float fallback)
        => cells.TryGetValue(field, out string raw) && float.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;
}
