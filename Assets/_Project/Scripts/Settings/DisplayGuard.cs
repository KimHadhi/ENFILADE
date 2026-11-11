using UnityEngine;

/// <summary>
/// Keeps the window the way the settings left it across Alt+Tab (user, 2026-11-11: "전체화면 찐빠 난 거 고치기 … 알트탭
/// 하면 뭔가 이상했던가?"). Leaving an exclusive full screen hands the monitor back to the desktop, and Windows can bring the
/// game back at the desktop's resolution, as a window, on the other monitor, or with the cursor free while the game still
/// thinks it holds it. So once the game has focus again it looks twice (DisplaySettings.Restore: half a second after,
/// then again at two seconds, for a monitor that is slow to change modes) and puts back what it finds moved. Every focus
/// change writes one line to Player.log, so a stray case can be read afterwards. Builds only, like DisplaySettings.
/// </summary>
[DisallowMultipleComponent]
public sealed class DisplayGuard : MonoBehaviour
{
    // Seconds after the focus comes back for the first and the second look.
    private const float FirstLook = .5f, SecondLook = 2f;
    private float firstAt = -1f, secondAt = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (!DisplaySettings.Applies) return;
        DontDestroyOnLoad(new GameObject("Display Guard").AddComponent<DisplayGuard>().gameObject);
    }

    private void OnApplicationFocus(bool focused)
    {
        DisplaySettings.Report(focused ? "focus back" : "focus lost");
        float now = Time.realtimeSinceStartup;
        firstAt = focused ? now + FirstLook : -1f;
        secondAt = focused ? now + SecondLook : -1f;
    }

    private void Update()
    {
        DisplaySettings.Tick();
        float now = Time.realtimeSinceStartup;
        if (firstAt >= 0f && now >= firstAt)
        {
            firstAt = -1f;
            DisplaySettings.Restore("focus back");
        }
        if (secondAt >= 0f && now >= secondAt)
        {
            secondAt = -1f;
            DisplaySettings.Restore("focus back, second look");
        }
    }
}
