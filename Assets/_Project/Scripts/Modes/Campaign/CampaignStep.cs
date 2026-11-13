using System;
using System.Collections.Generic;

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
/// manned (chapter 4's pushback) makes a hold's clock run only while someone stands within radius of its spot.
/// </summary>
public sealed class CampaignStep
{
    public const string TableName = "CampaignSteps";

    public string Id, Spot, Title, Description, Done, Prompt, Music;
    public CampaignStepKind Kind;
    public float Radius, Seconds, Rate;
    public int Horde, Order, Count, PeakBosses;
    /// <summary>
    /// A hold that goes on only while someone is at its spot (the airport's tug: nobody at the controls, nothing moves).
    /// Leaving pauses it and takes nothing back, so stepping away to fight costs time, never progress.
    /// </summary>
    public bool Manned;

    public static List<CampaignStep> For(string chapter)
    {
        var steps = new List<CampaignStep>();
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (!GameTables.TryGetRow(TableName, id, out var row) || !string.Equals(row.Text("chapter"), chapter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!Enum.TryParse(row.Text("kind"), true, out CampaignStepKind kind)) continue;
            // The words are Texts rows (mission.<step>.title …) the cells name, or plain words in a modder's table.
            steps.Add(new CampaignStep
            {
                Id = id, Kind = kind, Spot = row.Text("spot"), Title = Texts.Cell(row.Text("title")),
                Description = Texts.Cell(row.Text("description")), Done = Texts.Cell(row.Text("done")),
                Prompt = Texts.Cell(row.Text("prompt")), Order = (int)row.Number("order", 0f),
                Radius = row.Number("radius", 4f), Seconds = row.Number("seconds", 0f),
                Horde = (int)row.Number("horde", 0f), Rate = row.Number("rate", 0f), Count = (int)row.Number("count", 1f),
                Music = row.Text("music"), PeakBosses = (int)row.Number("peakBoss", 0f), Manned = row.Number("manned", 0f) > 0f
            });
        }
        steps.Sort((a, b) => a.Order.CompareTo(b.Order));
        return steps;
    }
}

/// <summary>
/// A chapter's story and what comes after it (Tables/CampaignChapters, one row per scene): the card shown when it
/// begins (intro) and ends (outro), and the next chapter's scene (empty after the last). The title, intro and outro
/// cells name Texts rows (chapter.&lt;scene&gt;.title …), where the words are written; each line of the story is a line
/// there (Enter in the 글자 편집기), as many as it needs (Texts.Lines).
/// </summary>
public sealed class CampaignChapter
{
    public const string TableName = "CampaignChapters";

    public string Title, Next;
    public string[] Intro, Outro;

    public static CampaignChapter For(string scene)
    {
        if (!GameTables.TryGetRow(TableName, scene, out var row)) return null;
        return new CampaignChapter
        {
            Title = Texts.Cell(row.Text("title")), Next = row.Text("next"),
            Intro = Texts.Lines(Texts.Cell(row.Text("intro"))), Outro = Texts.Lines(Texts.Cell(row.Text("outro")))
        };
    }
}
