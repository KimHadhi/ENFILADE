using System;
using System.IO;
using UnityEditor;

/// <summary>
/// Lets a tool outside Unity have the open editor play the scene that is open and run the legs' test in it (MoveTest), with
/// nobody at the editor: make the file Temp/enfilade-movetest.request. Within half a second, once nothing compiles and
/// nothing plays, the editor deletes it, leaves Temp/enfilade-movetest.running for the test to find and enters play; the
/// test writes Logs/move-test.txt and ends the play. A play that ends otherwise takes the marker with it, so the next play
/// by hand is no test. A line only=climb,jog W in the request runs only those moves, and film=slide pictures those
/// (MoveFilm, Logs/MoveTest); the marker carries the request on.
/// </summary>
[InitializeOnLoad]
internal static class MoveTestOnRequest
{
    private const string Request = "Temp/enfilade-movetest.request", Running = "Temp/enfilade-movetest.running";
    private static double next;

    static MoveTestOnRequest()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += change =>
        {
            if (change == PlayModeStateChange.EnteredEditMode) Delete(Running);
        };
    }

    private static void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < next) return;
        next = now + .5;
        if (!File.Exists(Request)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string asked;
        try
        {
            asked = File.ReadAllText(Request);
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            return;
        }
        if (!Delete(Request)) return;
        try
        {
            File.WriteAllText(Running, DateTime.Now.ToString("HH:mm:ss") + "\n" + asked);
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            return;
        }
        EditorApplication.EnterPlaymode();
    }

    private static bool Delete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            return false;
        }
    }
}
