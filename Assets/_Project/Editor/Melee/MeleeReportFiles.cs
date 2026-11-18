using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps the melee reports in Logs/ as the game would behave right now, with nobody pressing anything: written after
/// every compile and whenever a table they read is saved, so a session (or the user) reads a file instead of playing to
/// find out (the user's standing demand: what was built is checked by whoever built it).
/// - Logs/execution-rules.txt: every rule of takedowns and executions and which one a player gets at each place round a
///   zombie (ExecutionRulesReport, the game's own ExecutionRules.Pick).
/// - Logs/knife-swings.txt: every class knife's stats, its clips' phases and what a tap, a hold and a mash turn into on
///   its own timeline (KnifeSwingReport, the game's own SwingTimeline).
/// The menu ENFILADE → 근접 writes them on demand and shows the file.
/// </summary>
[InitializeOnLoad]
internal sealed class MeleeReportFiles : AssetPostprocessor
{
    private const string Folder = "Logs", RulesFile = "execution-rules.txt", SwingsFile = "knife-swings.txt";

    static MeleeReportFiles() => EditorApplication.delayCall += WriteAll;

    // After the table watcher has had the game read the tables again (GameTablesWatcher, in the same pass): next tick.
    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (imported.Concat(moved).Any(IsTable)) EditorApplication.delayCall += WriteAll;
    }

    private static bool IsTable(string path)
        => path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) && path.IndexOf("/Tables/", StringComparison.OrdinalIgnoreCase) >= 0;

    [MenuItem("ENFILADE/근접/암살·처형 규칙 보고서")]
    private static void ShowRules()
    {
        WriteAll();
        EditorUtility.RevealInFinder(Path.Combine(Folder, RulesFile));
    }

    [MenuItem("ENFILADE/근접/칼 휘두르기 보고서")]
    private static void ShowSwings()
    {
        WriteAll();
        EditorUtility.RevealInFinder(Path.Combine(Folder, SwingsFile));
    }

    private static void WriteAll()
    {
        Write(RulesFile, ExecutionRulesReport.Text);
        Write(SwingsFile, KnifeSwingReport.Text);
    }

    // A report that can't be made or written says so once in the console and leaves the old file alone.
    private static void Write(string file, Func<string> text)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, file), text(), new UTF8Encoding(false));
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            Debug.LogWarning($"[Melee] {file} 보고서를 쓰지 못했습니다: {error.Message}");
        }
    }
}
