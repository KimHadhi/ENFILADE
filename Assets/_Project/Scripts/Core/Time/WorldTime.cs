using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// How fast the world runs, decided in one place. Whatever slows the game holds a speed here under its own name: a
/// kill streak's skull (SkullSlowMotion), the counter's slow motion and the hit stops (CombatTimeEffects, alone). The
/// world runs at the slowest speed anyone holds, at 1 when nobody does, and not at all while the game is paused
/// (GamePause). So two slowdowns never fight over the number, a pause has nothing to remember and put back, and a holder
/// destroyed while it holds (a scene left in the middle of a slow motion) lets go by itself.
/// The physics step shrinks with the speed, so a slowed world steps as often per real second as a fast one and still
/// moves smoothly. Nothing else writes Time.timeScale or Time.fixedDeltaTime.
/// Before 0.9.4 the pause and the two slowdowns each wrote the time scale themselves, each keeping its own copy of what
/// to go back to and guessing from a changed number that another had taken over.
/// </summary>
public static class WorldTime
{
    private struct Held { public Object Owner; public float Speed; }

    private static readonly List<Held> holds = new List<Held>();
    // The project's physics step at full speed (Project Settings > Time), read when play starts.
    private static float step = 1f / 120f;
    private static bool stopped;

    /// <summary>The speed in force: the slowest one held, 1 with none, 0 while paused.</summary>
    public static float Speed { get; private set; } = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        holds.Clear();
        stopped = false;
        Speed = 1f;
        Time.timeScale = 1f;
        step = Time.fixedDeltaTime;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        Application.quitting -= Restore;
        Application.quitting += Restore;
    }

    /// <summary>
    /// Holds the world at this speed (above 0, up to 1) until the owner lets go or is destroyed. Asking again changes
    /// the owner's speed, so a slowdown that eases in and out asks every frame.
    /// </summary>
    public static void Hold(Object owner, float speed)
    {
        if (owner == null) return;
        speed = Mathf.Clamp(speed, .01f, 1f);
        int at = IndexOf(owner);
        if (at >= 0 && holds[at].Speed == speed) return;
        var held = new Held { Owner = owner, Speed = speed };
        if (at >= 0) holds[at] = held;
        else holds.Add(held);
        Apply();
    }

    /// <summary>Lets go of the owner's hold; nothing happens when it held none.</summary>
    public static void Release(Object owner)
    {
        int at = IndexOf(owner);
        if (at < 0) return;
        holds.RemoveAt(at);
        Apply();
    }

    /// <summary>Stops the world, or lets it run again at whatever speed is held. GamePause's alone: it counts who pauses.</summary>
    internal static void Stop(bool stop)
    {
        if (stopped == stop) return;
        stopped = stop;
        Apply();
    }

    // By reference: an owner that is being destroyed must still find its own hold to let go of it.
    private static int IndexOf(Object owner)
    {
        for (int i = 0; i < holds.Count; i++)
            if (ReferenceEquals(holds[i].Owner, owner)) return i;
        return -1;
    }

    private static void Apply()
    {
        float speed = 1f;
        for (int i = holds.Count - 1; i >= 0; i--)
        {
            if (holds[i].Owner == null) holds.RemoveAt(i);
            else if (holds[i].Speed < speed) speed = holds[i].Speed;
        }
        Speed = stopped ? 0f : speed;
        Time.timeScale = Speed;
        // Under a pause the step is the one for the speed the world goes back to, so the first frame after it steps right.
        Time.fixedDeltaTime = step * speed;
    }

    // Whatever the scene's own things held went with them.
    private static void OnSceneUnloaded(Scene scene) => Apply();

    // Leaving Play in the editor: the project's own numbers back, so the next Play starts from them.
    private static void Restore()
    {
        holds.Clear();
        stopped = false;
        Speed = 1f;
        Time.timeScale = 1f;
        Time.fixedDeltaTime = step;
    }
}
