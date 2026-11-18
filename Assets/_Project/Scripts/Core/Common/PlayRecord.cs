using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// dev-text: file (lines for whoever reads a play's record afterwards; nothing here is shown in the game)

/// <summary>
/// A feature's own record of what it did in a play, for whoever has to check it afterwards without having played:
/// Logs/&lt;name&gt;.txt beside the project in the editor, under the player's own folder in a development build, nothing at
/// all in a release build. Each file starts afresh with the first line a play writes to it (its heading says what the
/// lines mean), and keeps the play before as &lt;name&gt;.previous.txt.
/// Made for the user's rule of 2026-11-15 (memory root-cause-fixes): a result must be checkable without Play by whoever
/// changed it, never handed to the user as a hunt. A feature that judges something (a placement refused, a grenade's
/// predicted and flown burst) writes one line per judgment here, with what it judged by.
/// </summary>
public static class PlayRecord
{
    private static readonly HashSet<string> started = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static bool failed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        started.Clear();
        failed = false;
    }

    /// <summary>Whether records are kept here: the editor and development builds.</summary>
    public static bool On => Application.isEditor || Debug.isDebugBuild;

    private static string Folder => Application.isEditor
        ? Path.Combine(Application.dataPath, "..", "Logs")
        : Path.Combine(Application.persistentDataPath, "Logs");

    /// <summary>
    /// Adds a line to Logs/<paramref name="name"/>.txt, stamped with the play's clock. The first line of a play starts
    /// the file with <paramref name="heading"/>.
    /// </summary>
    public static void Line(string name, string heading, string line)
    {
        if (!On || failed || string.IsNullOrEmpty(name)) return;
        try
        {
            string path = Path.Combine(Folder, name + ".txt");
            if (started.Add(name))
            {
                Directory.CreateDirectory(Folder);
                if (File.Exists(path)) File.Copy(path, Path.Combine(Folder, name + ".previous.txt"), true);
                File.WriteAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {heading}\n", new UTF8Encoding(false));
            }
            File.AppendAllText(path, $"{Time.time,8:0.00}s {line}\n", new UTF8Encoding(false));
        }
        catch (Exception problem) when (problem is IOException || problem is UnauthorizedAccessException || problem is NotSupportedException)
        {
            // A record that cannot be written must never cost the play anything: said once, then silent.
            failed = true;
            Debug.LogWarning("[PlayRecord] Logs/" + name + ".txt: " + problem.Message);
        }
    }

    /// <summary>A point as the records write it (m, two decimals).</summary>
    public static string At(Vector3 point) => $"({point.x:0.00}, {point.y:0.00}, {point.z:0.00})";

    /// <summary>An object's place in the scene as the records write it: its own name under its parents', from the root down.</summary>
    public static string PathOf(Transform leaf)
    {
        if (leaf == null) return "(none)";
        var names = new List<string>();
        for (var at = leaf; at != null; at = at.parent) names.Add(at.name);
        names.Reverse();
        // The first folders of the runtime hierarchy say nothing about what it is; the last few names do.
        int from = Mathf.Max(0, names.Count - 4);
        return (from > 0 ? "…/" : "") + string.Join("/", names.GetRange(from, names.Count - from));
    }
}
