using UnityEngine;

/// <summary>Applies the frame rate cap and vSync from <see cref="GameSettings"/> before the first scene and on every change.</summary>
public static class FrameRateLimiter
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Startup()
    {
        GameSettings.Changed += Apply;
        Apply();
    }

    // Unity ignores targetFrameRate while vSyncCount is above 0, so vSync wins over the cap.
    private static void Apply()
    {
        var settings = GameSettings.Current;
        QualitySettings.vSyncCount = settings.vSync ? 1 : 0;
        Application.targetFrameRate = settings.frameLimit > 0 ? settings.frameLimit : -1;
    }
}
