#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;

/// <summary>
/// Runs one of the editor's checks that need no play (AmmoCheck, MuzzleFlashCheck): once after every script reload and
/// after a play, so its report is always of the code and tables as they are now, and again whenever its request file is
/// made (a tool outside Unity asking, as RefreshOnRequest is asked). Never while the game plays, compiles or imports.
/// The checks are the only way whoever changes a rule without being able to play can see whether it holds, so they run
/// by themselves rather than waiting to be remembered.
/// </summary>
internal sealed class CheckOnRequest
{
    private readonly string request;
    private readonly Action run;
    private double next;
    private bool ran;

    /// <summary>Watches for the file <paramref name="request"/> (under the project folder) and runs <paramref name="run"/>.</summary>
    public CheckOnRequest(string request, Action run)
    {
        this.request = request;
        this.run = run;
        EditorApplication.update += Tick;
    }

    private void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < next) return;
        next = now + .5;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        bool asked = File.Exists(request);
        if (!asked && ran) return;
        if (asked)
        {
            try
            {
                File.Delete(request);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                return;
            }
        }
        ran = true;
        run();
    }
}
#endif
