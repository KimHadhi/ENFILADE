using System;
using System.Collections.Generic;
using System.Text;

// dev-text: file (lines for whoever reads Logs/callouts.txt afterwards; nothing here is shown in the game)

/// <summary>
/// The callout tables read through, so what is wrong in them is told rather than found by playing (console /callouts →
/// Logs/callouts.txt). Problems: a condition naming a fact there is none of (such a row fits nothing, silently), a
/// kind or a phase no zombie or campaign has, a line whose slot no slice says, a slice with nothing to say, a page
/// nothing opens or that is empty, a place the wheel doesn't have. Then, for the player at the console: which facts hold
/// now and what every slice of every page would say, with the line's id, so a new row is seen to win or lose where it
/// should without acting the moment out.
/// </summary>
public static class CalloutCheck
{
    public const string Log = "callouts";
    private const string Auto = "auto", Heading = "callout tables: problems, then what fits now (console /callouts)";

    /// <summary>What is wrong in Callouts.csv and CalloutSlots.csv, a line each; empty when nothing is.</summary>
    public static List<string> Problems()
    {
        var problems = new List<string>();
        var pages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var opened = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { CalloutSlots.FirstPage };
        var said = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in CalloutLines.All)
        {
            said.Add(line.Slot);
            // A shout by itself is called by its id; its condition is not read.
            if (!string.Equals(line.Slot, Auto, StringComparison.OrdinalIgnoreCase)) Words(problems, "Callouts.csv [" + line.Id + "]", line.When);
        }
        var slices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in CalloutSlots.All)
        {
            string where = "CalloutSlots.csv [" + slot.Id + "]";
            slices.Add(slot.Id);
            pages[slot.Page] = pages.TryGetValue(slot.Page, out int count) ? count + 1 : 1;
            Words(problems, where, slot.When);
            if (slot.Place < 0 || slot.Place >= CalloutSlots.Places)
                problems.Add($"{where} place {slot.Place}: the wheel has places 0 to {CalloutSlots.Places - 1}");
            if (!string.IsNullOrEmpty(slot.Opens)) opened.Add(slot.Opens);
            else if (!said.Contains(slot.Id)) problems.Add($"{where}: no line of Callouts.csv has this slot, and it opens no page: an empty slice");
        }
        foreach (var slot in CalloutSlots.All)
            if (!string.IsNullOrEmpty(slot.Opens) && !pages.ContainsKey(slot.Opens))
                problems.Add($"CalloutSlots.csv [{slot.Id}] opens '{slot.Opens}': no slice is on that page");
        foreach (var page in pages.Keys)
            if (!opened.Contains(page)) problems.Add($"CalloutSlots.csv page '{page}': nothing opens it");
        foreach (var line in CalloutLines.All)
            if (!slices.Contains(line.Slot) && !string.Equals(line.Slot, Auto, StringComparison.OrdinalIgnoreCase))
                problems.Add($"Callouts.csv [{line.Id}] slot '{line.Slot}': no slice of CalloutSlots.csv says it");
        return problems;
    }

    // The facts a condition names that there are none of, and the words after a colon that can be checked here.
    private static void Words(List<string> problems, string where, string when)
    {
        foreach (var term in CalloutWhen.Terms(when))
        {
            if (!CalloutFacts.Known(term.Name))
                problems.Add($"{where} when '{when}': no fact is called '{term.Name}' (the row fits nothing)");
            else if ((term.Name == "kind" || term.Name == "crawl") && !Enum.TryParse(term.Word, true, out ZombieKind _))
                problems.Add($"{where} when '{when}': no zombie kind is called '{term.Word}'");
            else if (term.Name == "phase" && !Enum.TryParse(term.Word, true, out CombatPhase _))
                problems.Add($"{where} when '{when}': no phase is called '{term.Word}' (quiet, control, anticipation, assault)");
        }
    }

    /// <summary>
    /// Writes the problems and, for this PC's player, what holds and what would be said now, to Logs/callouts.txt.
    /// Returns a line for the console.
    /// </summary>
    public static string Write(PlayerCallouts mine)
    {
        var problems = Problems();
        PlayRecord.Line(Log, Heading, $"== {CalloutLines.All.Count} lines, {CalloutSlots.All.Count} slices, {problems.Count} problem(s)");
        foreach (string problem in problems) PlayRecord.Line(Log, Heading, "PROBLEM " + problem);
        if (mine == null) return Summary(problems, "");
        var sense = mine.SenseNow();
        var holds = new StringBuilder();
        foreach (string name in CalloutFacts.Names)
            if (!Takes(name) && CalloutFacts.Holds(sense, name, "") == true) holds.Append(holds.Length > 0 ? ", " : "").Append(name);
        PlayRecord.Line(Log, Heading, $"NOW class {PlayerClass.CurrentId}, phase {CampaignRun.Phase}, target {(sense.Target != null ? sense.TargetKind : "none")}"
            + $"{(sense.Aimed ? " (aimed)" : "")}, awake near {sense.Crowd}/{sense.Close}, health {sense.HealthShare:0.00}; facts that hold: {holds}");
        var slots = new CalloutSlots.Slot[CalloutSlots.Places];
        CalloutWhen.Fact fact = (name, word) => CalloutFacts.Holds(sense, name, word);
        Func<string, bool> fits = when => CalloutWhen.Fits(when, fact);
        var pages = new List<string> { CalloutSlots.FirstPage };
        for (int p = 0; p < pages.Count; p++)
        {
            CalloutSlots.Resolve(pages[p], fits, slots);
            for (int place = 0; place < slots.Length; place++)
            {
                var slot = slots[place];
                if (slot == null) { PlayRecord.Line(Log, Heading, $"  {pages[p]} {place}: (empty)"); continue; }
                if (!string.IsNullOrEmpty(slot.Opens))
                {
                    if (!pages.Contains(slot.Opens)) pages.Add(slot.Opens);
                    PlayRecord.Line(Log, Heading, $"  {pages[p]} {place}: {slot.Id} \"{slot.Title}\" -> opens page {slot.Opens}");
                    continue;
                }
                var line = CalloutLines.Pick(slot.Id, fits, 0);
                PlayRecord.Line(Log, Heading, $"  {pages[p]} {place}: {slot.Id} \"{slot.Title}\" -> "
                    + (line != null ? $"{line.Id} (when {line.When}, order {line.Order}) \"{line.Text}\"" : "(no line fits)"));
            }
        }
        return Summary(problems, " + what fits now");
    }

    // Facts that need a word after their colon say nothing asked bare.
    private static bool Takes(string name) => name == "kind" || name == "crawl" || name == "class" || name == "team" || name == "phase" || name == "step";

    private static string Summary(List<string> problems, string also)
        => $"Logs/{Log}.txt: {CalloutLines.All.Count} lines, {CalloutSlots.All.Count} slices, {problems.Count} problem(s){also}"
            + (problems.Count > 0 ? "\n" + string.Join("\n", problems.GetRange(0, Math.Min(6, problems.Count))) : "");
}
