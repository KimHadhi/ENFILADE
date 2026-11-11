using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>What one step of a campaign chapter asks of the players (Tables/CampaignSteps, kind).</summary>
public enum CampaignStepKind
{
    /// <summary>Someone gets to the spot.</summary>
    Reach,
    /// <summary>Someone presses Interact at the spot.</summary>
    Press,
    /// <summary>Hold out for the step's seconds while the horde comes.</summary>
    Hold,
    /// <summary>Everyone still alive gets to the spot (a rest stop). From then on the dead come back here.</summary>
    Checkpoint,
    /// <summary>Everyone still alive gets to the spot, nothing more: waiting for each other before a way with no return.</summary>
    Gather,
    /// <summary>The step's count of fuel cans poured at the spot (CampaignCans).</summary>
    Collect,
    /// <summary>Everyone still alive gets to the spot, and the chapter is done.</summary>
    Escape
}

/// <summary>
/// One step of a chapter, read from Tables/CampaignSteps: the rows whose chapter is the scene's name, in their order.
/// spot names an object under the scene's 진행 자리 (CampaignRun.spots); radius is how near counts (m); seconds is a
/// hold's length; count is how many cans a Collect wants; horde and rate are how many enemies may be about during the
/// step and how many come per second. music names a song in Tables/Music played while the step runs (the next step
/// naming the same song lets it play on), and peakBoss is how many brutes come at that song's highlight (CampaignMusic).
/// mix (bundle 3) names the step's row in Tables/CampaignMix, what its zombies are made of and what fills its boss slot;
/// a step that names none keeps the mix of the step before it.
/// </summary>
public sealed class CampaignStep
{
    public const string TableName = "CampaignSteps";

    public string Id, Spot, Title, Description, Done, Prompt, Music, Mix;
    public CampaignStepKind Kind;
    public float Radius, Seconds, Rate;
    public int Horde, Order, Count, PeakBosses;

    public static List<CampaignStep> For(string chapter)
    {
        var steps = new List<CampaignStep>();
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (!GameTables.TryGetRow(TableName, id, out var row) || !string.Equals(Text(row, "chapter"), chapter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!Enum.TryParse(Text(row, "kind"), true, out CampaignStepKind kind)) continue;
            steps.Add(new CampaignStep
            {
                Id = id, Kind = kind, Spot = Text(row, "spot"), Title = Text(row, "title"), Description = Text(row, "description"),
                Done = Text(row, "done"), Prompt = Text(row, "prompt"), Order = (int)Number(row, "order", 0f),
                Radius = Number(row, "radius", 4f), Seconds = Number(row, "seconds", 0f),
                Horde = (int)Number(row, "horde", 0f), Rate = Number(row, "rate", 0f), Count = (int)Number(row, "count", 1f),
                Music = Text(row, "music"), PeakBosses = (int)Number(row, "peakBoss", 0f), Mix = Text(row, "mix")
            });
        }
        steps.Sort((a, b) => a.Order.CompareTo(b.Order));
        // A step that names no mix keeps the one before it, so a mix holds from where it is named on.
        for (int i = 1; i < steps.Count; i++) if (steps[i].Mix.Length == 0) steps[i].Mix = steps[i - 1].Mix;
        return steps;
    }

    internal static string Text(IReadOnlyDictionary<string, string> row, string key)
        => row.TryGetValue(key, out string text) ? text.Trim() : "";

    internal static float Number(IReadOnlyDictionary<string, string> row, string key, float fallback)
        => row.TryGetValue(key, out string text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value : fallback;
}

/// <summary>
/// A chapter's story and what comes after it (Tables/CampaignChapters, one row per scene): the card shown when it
/// begins (intro) and ends (outro), lines split by '|', and the next chapter's scene (empty after the last).
/// </summary>
public sealed class CampaignChapter
{
    public const string TableName = "CampaignChapters";
    private static readonly char[] LineBreak = { '|' };

    public string Title, Next;
    public string[] Intro, Outro;

    public static CampaignChapter For(string scene)
    {
        if (!GameTables.TryGetRow(TableName, scene, out var row)) return null;
        return new CampaignChapter
        {
            Title = CampaignStep.Text(row, "title"), Next = CampaignStep.Text(row, "next"),
            Intro = Lines(CampaignStep.Text(row, "intro")), Outro = Lines(CampaignStep.Text(row, "outro"))
        };
    }

    private static string[] Lines(string text)
    {
        var lines = text.Split(LineBreak, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++) lines[i] = lines[i].Trim();
        return lines;
    }
}
