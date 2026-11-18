using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// While the game's window is not the one in front, says now and then how the game runs there: frames in the last few
/// seconds, the longest of them, the scene, whether a room is hosted and whether the loading screen is up.
/// Nothing in the game stops on losing focus (Run In Background is on, no script pauses), so when a host's progress
/// stalls behind another window (user, 2026-11-18: "방장(호스트) 알트탭 시 허브 및 맵 전환 진행이 멈추는 현상") it is the
/// frames themselves that thin out there: an editor that idles when it is not in front, a window Windows stops
/// presenting. That cannot be told from the code; these lines tell it from the next play (Player.log in a build, the
/// Console in the editor). A dozen lines a stretch at most.
/// </summary>
[DisallowMultipleComponent]
public sealed class FocusPulse : MonoBehaviour
{
    // A line this often while away (s), and no more than this many in one stretch away.
    private const float Every = 5f;
    private const int MostLines = 12;
    private bool away;
    private float since, longest;
    private int frames, lines;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create() => RuntimeSceneHierarchy.Keep(new GameObject("Focus Pulse").AddComponent<FocusPulse>().gameObject);

    private void OnApplicationFocus(bool focused)
    {
        if (!focused && !away)
        {
            away = true;
            lines = 0;
            Restart();
        }
        else if (focused && away)
        {
            Say("in front again");
            away = false;
        }
    }

    private void Update()
    {
        if (!away) return;
        frames++;
        longest = Mathf.Max(longest, Time.unscaledDeltaTime);
        if (Time.realtimeSinceStartup - since < Every) return;
        if (lines < MostLines) Say("behind another window");
        Restart();
    }

    private void Restart()
    {
        since = Time.realtimeSinceStartup;
        frames = 0;
        longest = 0f;
    }

    private void Say(string what)
    {
        lines++;
        float span = Mathf.Max(.001f, Time.realtimeSinceStartup - since);
        string room = GameSession.IsOnline ? (GameSession.IsGuest ? "a guest" : "hosting") : "alone";
        Debug.Log($"[Focus] {what}: {frames} frames in {span:0.0} s ({frames / span:0} a second), longest {longest * 1000f:0} ms, "
            + $"scene '{SceneManager.GetActiveScene().name}', {room}{(LoadingScreen.Visible ? ", loading screen up" : "")}");
    }
}
