using System;
using System.IO;
using UnityEditor;

/// <summary>
/// Lets a tool outside Unity ask the open editor to take in changed files and recompile, without anyone clicking the
/// editor's window (Unity only looks at the disk when it gets the focus). To ask: make the file
/// Temp/enfilade-refresh.request. The editor sees it within half a second, deletes it and refreshes; what the compile
/// said is in the Editor.log as always. Temp/enfilade-refresh.done holds the last thing this did and when ("loaded"
/// after every script reload, so a reload that happened is a compile that passed; "refresh" when a request was taken).
/// A request waits while the game is playing, so a play test is never interrupted.
/// </summary>
[InitializeOnLoad]
internal static class RefreshOnRequest
{
    private const string Request = "Temp/enfilade-refresh.request", Done = "Temp/enfilade-refresh.done";
    private static double next;

    static RefreshOnRequest()
    {
        EditorApplication.update += Tick;
        Note("loaded");
    }

    private static void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < next) return;
        next = now + .5;
        if (!File.Exists(Request)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            File.Delete(Request);
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            return;
        }
        Note("refresh");
        AssetDatabase.Refresh();
    }

    private static void Note(string what)
    {
        try
        {
            File.WriteAllText(Done, what + " " + DateTime.Now.ToString("HH:mm:ss.fff"));
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
        }
    }
}
