using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Whether each callout's stand-in voice still says its line. The voices are Windows TTS clips made from the lines'
/// Korean words by Tools/Audio/callout_voices.py (Art/Audio/Voice/Hiro/Callout/&lt;id&gt;/&lt;id&gt;_tts.wav), and the script
/// keeps what each clip says in Tools/Audio/callout_voices_spoken.csv. A line rewritten in the 글자 편집기 no longer
/// matches its clip until the script speaks it again: this tells which ones and runs the script for just those
/// (<see cref="RemakeChanged"/>, the same as typing "python Tools/Audio/callout_voices.py --changed" in the project
/// folder). A folder holding a real recording is never touched by the script, so its line shows as recorded.
/// </summary>
internal static class CalloutVoices
{
    internal enum State
    {
        /// <summary>The clip says the line as it is.</summary>
        Fresh,
        /// <summary>The line was rewritten after its clip was made.</summary>
        Stale,
        /// <summary>No clip yet (a new line).</summary>
        Missing,
        /// <summary>A real recording sits in its folder: the script leaves it alone.</summary>
        Recorded
    }

    private const string Script = "Tools/Audio/callout_voices.py";
    private const string Spoken = "Tools/Audio/callout_voices_spoken.csv";
    private const string Folder = "Assets/_Project/Art/Audio/Voice/Hiro/Callout";
    private const string Table = "Assets/_Project/Resources/Tables/" + CalloutLines.TableName + ".csv";

    private static Process running;
    private static readonly StringBuilder output = new StringBuilder();

    /// <summary>Whether the script is making voices right now.</summary>
    internal static bool Busy => running != null;

    internal static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

    /// <summary>
    /// Each callout line's Texts id (what its row's text cell names; a row with plain words has none) and line id. Read
    /// from the file each time: the editor asks only when it draws the callout rows.
    /// </summary>
    internal static Dictionary<string, List<string>> LinesByText()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string path = Path.Combine(ProjectRoot, Table);
        if (!File.Exists(path)) return map;
        var records = CsvTable.ReadRecords(TableFile.Read(Table));
        if (records.Count == 0) return map;
        int column = TextColumn(records[0]);
        if (column < 0) return map;
        foreach (var record in records.Skip(1))
        {
            string id = record[0].Trim();
            if (id.Length == 0 || id.StartsWith("#") || column >= record.Count) continue;
            string cell = record[column].Trim();
            if (cell.Length == 0) continue;
            if (!map.TryGetValue(cell, out var lines)) map[cell] = lines = new List<string>();
            lines.Add(id);
        }
        return map;
    }

    // The text column: "text" (a Texts id since 2026-11-12), or "ko" in a table from before.
    private static int TextColumn(List<string> header)
    {
        int text = header.FindIndex(h => string.Equals(h.Trim(), "text", StringComparison.OrdinalIgnoreCase));
        return text >= 0 ? text : header.FindIndex(h => string.Equals(h.Trim(), Texts.Korean, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>What each line's clip says, as the script last wrote it down (line id → words).</summary>
    internal static Dictionary<string, string> SpokenWords()
    {
        var spoken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string path = Path.Combine(ProjectRoot, Spoken);
        if (!File.Exists(path)) return spoken;
        string text;
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(file, Encoding.UTF8, true))
            text = reader.ReadToEnd();
        foreach (var record in CsvTable.ReadRecords(text).Skip(1))
            if (record.Count >= 2 && record[0].Trim().Length > 0) spoken[record[0].Trim()] = record[1];
        return spoken;
    }

    /// <summary>The state of one line's voice, given the words it should say now (the Korean words as saved).</summary>
    internal static State StateOf(string lineId, string words, Dictionary<string, string> spoken)
    {
        string folder = Path.Combine(ProjectRoot, Folder, lineId);
        if (Directory.Exists(folder) && Directory.GetFiles(folder).Any(IsRecording)) return State.Recorded;
        if (!File.Exists(Path.Combine(folder, lineId + "_tts.wav"))) return State.Missing;
        return spoken.TryGetValue(lineId, out string said) && said.Trim() == (words ?? "").Trim() ? State.Fresh : State.Stale;
    }

    private static bool IsRecording(string file)
    {
        string name = Path.GetFileName(file).ToLowerInvariant();
        return (name.EndsWith(".wav") || name.EndsWith(".ogg") || name.EndsWith(".mp3")) && !name.EndsWith("_tts.wav");
    }

    internal static string Describe(State state) => state switch
    {
        State.Fresh => "목소리: 지금 글자 그대로",
        State.Stale => "목소리: 옛 글자를 말해요 → 바뀐 목소리 다시 만들기",
        State.Missing => "목소리: 아직 없어요 → 바뀐 목소리 다시 만들기",
        _ => "목소리: 녹음 파일이 있어요(스크립트가 건드리지 않아요)"
    };

    /// <summary>
    /// Runs the script for the lines whose clip is missing or says old words (Windows' Korean voice "Microsoft Heami
    /// Desktop", a few seconds a line), then imports what it wrote. Needs Python on the PATH; without it the dialog says
    /// the command to type.
    /// </summary>
    internal static void RemakeChanged(Action done)
    {
        if (running != null) return;
        output.Clear();
        foreach (string python in new[] { "python", "py" })
        {
            try
            {
                var start = new ProcessStartInfo(python, $"-X utf8 \"{Script}\" --changed")
                {
                    WorkingDirectory = ProjectRoot, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                start.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                running = new Process { StartInfo = start, EnableRaisingEvents = true };
                running.OutputDataReceived += (_, line) => { if (line.Data != null) lock (output) output.AppendLine(line.Data); };
                running.ErrorDataReceived += (_, line) => { if (line.Data != null) lock (output) output.AppendLine(line.Data); };
                running.Start();
                running.BeginOutputReadLine();
                running.BeginErrorReadLine();
                break;
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception || error is InvalidOperationException)
            {
                running = null;
            }
        }
        if (running == null)
        {
            EditorUtility.DisplayDialog("콜아웃 목소리", "파이썬을 찾지 못했어요. 프로젝트 폴더에서 직접 실행해 주세요:\n\npython " + Script + " --changed", "확인");
            return;
        }
        void Wait()
        {
            if (running != null && !running.HasExited) return;
            EditorApplication.update -= Wait;
            int code = running?.ExitCode ?? -1;
            running?.Dispose();
            running = null;
            AssetDatabase.Refresh();
            string said;
            lock (output) said = output.ToString().Trim();
            if (code == 0) Debug.Log("[콜아웃 목소리] 바뀐 줄의 목소리를 다시 만들었어요.\n" + said);
            else Debug.LogWarning($"[콜아웃 목소리] 스크립트가 실패했어요(코드 {code}).\n" + said);
            EditorUtility.DisplayDialog("콜아웃 목소리", code == 0 ? Last(said, 1200) : "실패했어요. 콘솔에 자세히 적었어요.\n\n" + Last(said, 900), "확인");
            done?.Invoke();
        }
        EditorApplication.update += Wait;
    }

    private static string Last(string text, int characters) => text.Length <= characters ? text : "…" + text.Substring(text.Length - characters);
}
