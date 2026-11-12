using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Every scene change the game makes by itself, alone or back to the title screen (HubFlow, CampaignRun, TowerRun,
/// GameSession, the settings menu, the console's /map): the loading screen comes up first, the scene is read in the
/// background while the screen shows how far it got, and it wakes only once the screen covers everything. A room's scene
/// changes are Netcode's own loads; SceneWarmup puts the screen up for those as they start.
/// Before (to 0.8.194) these were SceneManager.LoadScene, which reads and wakes the whole scene inside one frame of the
/// old one: the 18:53 run of 2026-11-12 froze 11.4 s going into the hub (Loading.ReadObject 8.7 s across threads,
/// LoadLevelAsync Integrate 4.2 s) with the old scene's last frame on the display. Reading now happens over many frames
/// behind the screen; waking (each object's Awake, which Unity runs in one frame) and the scene's first Update still make
/// a long frame, but under the screen, and SceneWarmup's work follows before the screen lifts.
/// Numbers: Tables/Loading.csv, Load row.
/// </summary>
public static class SceneLoad
{
    public const string TableName = "Loading", TableRow = "Load";
    /// <summary>Share of the loading screen's bar that reading the scene fills; SceneWarmup fills the rest.</summary>
    public const float ReadShare = .45f;
    // Seconds to wait for the screen to cover the display before loading anyway.
    private const float CoverWait = 2f;

    /// <summary>A load started here is still going (until the new scene has woken).</summary>
    public static bool Busy { get; private set; }

    /// <summary>The scene being loaded, while Busy.</summary>
    public static string Target { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Busy = false;
        Target = null;
    }

    /// <summary>
    /// Loads <paramref name="scene"/> in place of the open ones, behind the loading screen. False when it can't (not
    /// playing, no name, or a load already going: a second press while the first loads does nothing).
    /// </summary>
    public static bool Go(string scene)
    {
        if (!Application.isPlaying || string.IsNullOrEmpty(scene) || Busy) return false;
        Busy = true;
        Target = scene;
        if (LoadingScreen.Run(Load(scene)) == null)
        {
            Busy = false;
            Target = null;
            SceneManager.LoadScene(scene);
        }
        return true;
    }

    private static IEnumerator Load(string scene)
    {
        LoadingScreen.Show(scene);
        LoadingScreen.Report(0f, "loading.stage.scene");
        // Up over the old scene first, so the frames the load takes show the screen and never the old scene stopped.
        for (float waited = 0f; !LoadingScreen.Covered && waited < CoverWait; waited += Time.unscaledDeltaTime) yield return null;

        var before = Application.backgroundLoadingPriority;
        Application.backgroundLoadingPriority = Priority();
        AsyncOperation load = null;
        try { load = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single); }
        catch (Exception error) { Debug.LogException(error); }
        if (load == null)
        {
            Application.backgroundLoadingPriority = before;
            Busy = false;
            Target = null;
            LoadingScreen.Hide();
            yield break;
        }
        // Read in the background; the scene wakes only when let (90% is Unity's "read, waiting to wake").
        load.allowSceneActivation = false;
        while (load.progress < .9f)
        {
            LoadingScreen.Report(ReadShare * load.progress / .9f, "loading.stage.scene");
            yield return null;
        }
        LoadingScreen.Report(ReadShare, "loading.stage.wake");
        // The step's words are drawn before the long frame that wakes the scene.
        yield return null;
        load.allowSceneActivation = true;
        while (!load.isDone) yield return null;
        Application.backgroundLoadingPriority = before;
        Busy = false;
        Target = null;
    }

    // How hard the loading thread works while the screen is up (Unity's own steps: Low, BelowNormal, Normal, High).
    // High hands the most of each frame to loading, which nobody sees under the screen.
    private static ThreadPriority Priority()
    {
        if (GameTables.TryGetRow(TableName, TableRow, out var row) && row.TryGetValue("priority", out string named)
            && Enum.TryParse(named.Trim(), true, out ThreadPriority picked))
            return picked;
        return ThreadPriority.High;
    }
}
