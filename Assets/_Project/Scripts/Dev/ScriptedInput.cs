using UnityEngine;

// dev-text: file (a test's stand-in for the keys and the mouse; nothing here is shown in the game)

/// <summary>
/// The keys and the mouse as a test drives them (MoveTest), in place of the player's: while On, the movement keys read Move,
/// the sprint key Sprint, and the view turns by what Turn gives it each frame (degrees), whether or not the game's window
/// has the focus, so a test runs with the editor behind another window. InputFocus, MovementInput and PlayerLook ask it
/// first; off, they read the player's own as before.
/// </summary>
internal static class ScriptedInput
{
    /// <summary>A test has the keys and the mouse.</summary>
    internal static bool On;
    /// <summary>The movement keys (x right, y ahead).</summary>
    internal static Vector2 Move;
    /// <summary>The sprint key is held.</summary>
    internal static bool Sprint;
    // The view's turn still to give (degrees, + to the right).
    private static float yaw;

    /// <summary>Turns the view by this much over the next frames as PlayerLook takes it (Turn).</summary>
    internal static void AddYaw(float degrees) => yaw += degrees;

    /// <summary>This frame's turn of the view (degrees): all of what is asked, once.</summary>
    internal static float Turn()
    {
        float now = yaw;
        yaw = 0f;
        return now;
    }

    /// <summary>The player has the keys and the mouse again.</summary>
    internal static void Off()
    {
        On = Sprint = false;
        Move = Vector2.zero;
        yaw = 0f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Off();
}
