using System;
using System.IO;
using UnityEngine;

// dev-text: file (log lines for whoever looks into a lost save; nothing here is shown in the game)

/// <summary>
/// The player's own files under persistentDataPath (settings, class growth, records, kill counts, gun setups, kept
/// looks): each is written whole or not at all, and a file that cannot be read gives way to the one saved before it.
/// <see cref="Write"/> puts the new text beside the old file and swaps the two, keeping the old one as name.bak, so a
/// crash or a power cut in the middle of a save leaves the last good file. <see cref="Read"/> hands the text to the
/// caller's reader; when the file is empty or the reader throws on it, the backup is read the same way and put back
/// in the file's place, and the broken file is kept as name.broken to look at.
/// Before 0.9.4 each of the seven wrote straight over its file, and one that failed to read a single time started
/// from nothing and saved that over it: a power cut during a save could cost every class level.
/// </summary>
public static class SaveFile
{
    private const string Backup = ".bak", Fresh = ".tmp", Broken = ".broken";

    /// <summary>Writes the text as the file, whole. False, with a warning, when the disk would not take it.</summary>
    public static bool Write(string path, string text)
    {
        string fresh = path + Fresh;
        try
        {
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(fresh, text ?? "");
            if (File.Exists(path)) File.Replace(fresh, path, path + Backup);
            else File.Move(fresh, path);
            return true;
        }
        catch (Exception problem) when (problem is IOException || problem is UnauthorizedAccessException || problem is NotSupportedException)
        {
            Debug.LogWarning($"[저장] {path} 에 쓰지 못했습니다: {problem.Message}");
            return false;
        }
    }

    /// <summary>
    /// What <paramref name="read"/> makes of the file's text; of the backup's when the file is there but empty or the
    /// reader throws on it or gives null. Null when there is no file (a fresh start, so deleting a file still resets
    /// it) or neither can be read.
    /// </summary>
    public static T Read<T>(string path, Func<string, T> read) where T : class
    {
        if (!File.Exists(path)) return null;
        var value = TryRead(path, read, out string problem);
        if (value != null) return value;
        string backup = path + Backup;
        value = TryRead(backup, read, out _);
        Debug.LogWarning($"[저장] {path} 를 읽지 못했습니다({problem}). "
            + (value != null ? "바로 앞에 저장한 사본으로 되돌렸습니다." : "앞에 저장한 사본도 없어 처음부터 시작합니다.") + " 읽지 못한 파일은 .broken으로 남겨 둡니다.");
        try
        {
            File.Copy(path, path + Broken, true);
            // The good copy takes the file's place at once: left for the next save, the swap would bury it under the broken one.
            if (value != null) File.Copy(backup, path, true);
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { }
        return value;
    }

    private static T TryRead<T>(string path, Func<string, T> read, out string problem) where T : class
    {
        problem = "파일이 없음";
        try
        {
            if (!File.Exists(path)) return null;
            string text = File.ReadAllText(path);
            problem = "파일이 비어 있음";
            return string.IsNullOrWhiteSpace(text) ? null : read(text);
        }
        // Whatever the reader throws on a half-written file (JsonUtility: ArgumentException) means the same: unreadable.
        catch (Exception error)
        {
            problem = error.Message;
            return null;
        }
    }
}
