#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// What one of the editor's rule checks found (CardDeckCheck, PlayerColourCheck; run by CheckOnRequest without a play):
/// rule by rule, how many cases kept it and, for a rule broken, the first cases that broke it. Written as a file whose
/// first line says the whole answer, "ALL KEPT" or how many broke, so whoever cannot play reads one line, and whoever
/// looks further finds a line per rule starting "ok" or "FAIL", the cases under it and any measurements it noted.
/// </summary>
internal sealed class CheckReport
{
    // How many broken cases a rule lists before it only counts them.
    private const int Listed = 12;

    private readonly StringBuilder text = new StringBuilder();
    private readonly List<string> failures = new List<string>(), notes = new List<string>();
    private string rule;
    private int kept, broken;

    /// <summary>Cases that broke a rule, in all.</summary>
    public int Failed { get; private set; }

    /// <summary>Rules begun.</summary>
    public int Rules { get; private set; }

    /// <summary>The cases after this belong to the rule <paramref name="name"/>: what must hold, in a sentence.</summary>
    public void Rule(string name)
    {
        Close();
        rule = name;
        Rules++;
    }

    /// <summary>One case of the rule: <paramref name="what"/> says it, for the report when it does not hold.</summary>
    public void Expect(bool holds, string what)
    {
        if (holds) { kept++; return; }
        Failed++;
        broken++;
        if (failures.Count < Listed) failures.Add("  FAIL " + what);
    }

    /// <summary>A line under the rule whether it holds or not: something measured on the way.</summary>
    public void Note(string line) => notes.Add("       " + line);

    /// <summary>The check itself threw: counted as a broken case of the rule it was in.</summary>
    public void Stopped(Exception problem)
    {
        if (rule == null) Rule("the check runs to its end");
        Expect(false, "the check stopped: " + problem);
    }

    private void Close()
    {
        if (rule == null) return;
        text.AppendLine((broken == 0 ? "ok   " : "FAIL ") + rule + "  (" + kept + " kept" + (broken > 0 ? ", " + broken + " broken" : "") + ")");
        foreach (string failure in failures) text.AppendLine(failure);
        if (broken > failures.Count) text.AppendLine("  … and " + (broken - failures.Count) + " more");
        foreach (string note in notes) text.AppendLine(note);
        failures.Clear();
        notes.Clear();
        kept = broken = 0;
        rule = null;
    }

    /// <summary>
    /// Writes the report to <paramref name="path"/> (under the project folder): the time, <paramref name="title"/>, how
    /// many rules, and "ALL KEPT" or how many cases broke; then the rules. A broken rule is also a warning in the console.
    /// </summary>
    public void Write(string path, string title)
    {
        Close();
        var all = new StringBuilder();
        all.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {title}, {Rules} rules: " + (Failed == 0 && Rules > 0 ? "ALL KEPT" : Failed + " BROKEN"));
        all.AppendLine();
        all.Append(text);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, all.ToString(), new UTF8Encoding(false));
        }
        catch (Exception problem) when (problem is IOException || problem is UnauthorizedAccessException)
        {
            Debug.LogWarning("[Check] " + path + ": " + problem.Message);
        }
        if (Failed > 0) Debug.LogWarning("[Check] " + title + ": " + Failed + " broken, see " + path);
    }
}
#endif
