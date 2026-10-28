using System;
using UnityEngine;

/// <summary>
/// Pauses the game for the Esc menu. Pausing keeps the current time scale, including a combat slowdown, and resuming
/// restores exactly that value, so CombatTimeEffects keeps its slowdown across the pause. Only this class sets zero.
/// </summary>
public static class GamePause
{
    private static float resumeScale = 1f;
    public static bool IsPaused { get; private set; }
    public static event Action<bool> Changed;

    public static void Set(bool paused)
    {
        if (paused == IsPaused) return;
        IsPaused = paused;
        if (paused)
        {
            resumeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
        }
        else Time.timeScale = resumeScale;
        AudioListener.pause = paused;
        Changed?.Invoke(paused);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsPaused = false;
        resumeScale = 1f;
        Changed = null;
        AudioListener.pause = false;
    }
}
