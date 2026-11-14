using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// A map's baked probe light (its Adaptive Probe Volume) whose data does not load must not take the frame rate with it.
/// The render pipeline loads a scene's probe cells inside its camera loop and tries again at every camera of every frame
/// for as long as the load is pending. With a baking set whose lists and data files had come apart (a bake Windows cut
/// short with error 1224; the map builder now tells of it, ProbeCellWriteGuard and GreyboxBuilder.BakeWrites) the loader
/// threw there each time: 5430 exceptions in the user's plays of 2026-11-14 (Editor.log), every one ending that frame's
/// rendering early and writing a stack trace, in the chapter the user called laggy. A build would do the same, since it
/// ships the same files.
/// So when the probe loader has thrown a few times in a scene, the scene's probe data is switched off; its pending
/// load goes with it (ProbeVolumePerSceneData.OnDisable takes the scene out of the loader's queue), so nothing throws
/// again. The map goes on lit by the sky's flat ambient light, as a map without probes is, and the log says so once.
/// Building the map again brings the probes back.
/// </summary>
public static class ProbeLightGuard
{
    // The loader's throws let through before the probes go: fewer could be one bad frame, and a pending load that
    // throws never clears by itself.
    private const int Tolerated = 3;
    // The probe loader, as an exception's stack trace names it.
    private const string Loader = "ProbeReferenceVolume";

    private static int thrown;
    private static bool switching;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Begin()
    {
        thrown = 0;
        switching = false;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // Each scene is counted by itself.
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => thrown = 0;

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Exception || switching || string.IsNullOrEmpty(stackTrace) || !stackTrace.Contains(Loader)) return;
        if (++thrown < Tolerated) return;
        thrown = 0;
        // Switching the data off unloads its set, which logs on its own account: those lines are not counted.
        switching = true;
        try
        {
            int off = 0;
            foreach (var data in Object.FindObjectsByType<ProbeVolumePerSceneData>(FindObjectsSortMode.None))
            {
                if (!data.enabled) continue;
                data.enabled = false;
                off++;
            }
            if (off > 0)
                Debug.LogWarning("[Probes] " + SceneManager.GetActiveScene().name + ": the baked probe light did not load (" + condition +
                                 "), so it is switched off for this visit and the map is lit by the sky's flat light. Build the map again " +
                                 "(ENFILADE menu) to bake its probes anew.");
        }
        finally
        {
            switching = false;
        }
    }
}
