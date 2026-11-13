using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The game stopped for a screen that stands in front of it alone: the Esc menu, the card pick. Each screen takes the
/// pause under its own name and lets go when it closes; the game runs again when nobody holds it. So two such screens
/// open at once never undo each other's pause (before 0.9.4 this was one on/off switch: closing the Esc menu over the
/// card pick set the game running behind the cards). A holder destroyed while it holds lets go by itself.
/// The stop itself is WorldTime's, so whatever slow motion was running carries on where it was when the pause ends.
/// Sound stops with it.
/// </summary>
public static class GamePause
{
    private static readonly List<Object> holders = new List<Object>();

    public static bool IsPaused { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        holders.Clear();
        IsPaused = false;
        AudioListener.pause = false;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    /// <summary>Stops the game for as long as this one holds it. Taking it twice is taking it once.</summary>
    public static void Take(Object who)
    {
        if (who == null) return;
        if (IndexOf(who) < 0) holders.Add(who);
        Apply();
    }

    /// <summary>Lets go of this one's pause; nothing happens when it held none.</summary>
    public static void Release(Object who)
    {
        int at = IndexOf(who);
        if (at < 0) return;
        holders.RemoveAt(at);
        Apply();
    }

    // By reference: a holder that is being destroyed must still find itself to let go.
    private static int IndexOf(Object who)
    {
        for (int i = 0; i < holders.Count; i++)
            if (ReferenceEquals(holders[i], who)) return i;
        return -1;
    }

    private static void Apply()
    {
        holders.RemoveAll(holder => holder == null);
        bool paused = holders.Count > 0;
        if (paused == IsPaused) return;
        IsPaused = paused;
        WorldTime.Stop(paused);
        AudioListener.pause = paused;
    }

    // A holder that went with its scene no longer holds.
    private static void OnSceneUnloaded(Scene scene) => Apply();
}
