using UnityEngine;

// dev-text: file (a test's stand-in for the keys and the mouse; nothing here is shown in the game)

/// <summary>
/// The keys and the mouse as a test drives them (MoveTest), in place of the player's: while On, the movement keys read Move,
/// the sprint key Sprint, the jump and crouch keys Jump and Crouch, and the view turns by what Turn gives it each frame
/// (degrees), whether or not the game's window has the focus, so a test runs with the editor behind another window.
/// InputFocus, MovementInput and PlayerLook ask it first; off, they read the player's own as before.
/// </summary>
internal static class ScriptedInput
{
    /// <summary>A test has the keys and the mouse.</summary>
    internal static bool On;
    /// <summary>The movement keys (x right, y ahead).</summary>
    internal static Vector2 Move;
    /// <summary>The sprint key is held.</summary>
    internal static bool Sprint;
    /// <summary>Walk mode is on (MovementInput.WalkMode), as the Caps Lock switch would have it.</summary>
    internal static bool Walk;
    /// <summary>The jump key is held (PressJump puts it down).</summary>
    internal static bool Jump;
    // The frame the jump key goes down on: the one after PressJump, when the movement next reads its keys.
    private static int jumpDown = -1;

    /// <summary>Puts the jump key down from the next frame; it stays held until Jump is let go.</summary>
    internal static void PressJump()
    {
        Jump = true;
        jumpDown = Time.frameCount + 1;
    }

    /// <summary>The jump key went down this frame.</summary>
    internal static bool JumpPressed => Jump && Time.frameCount == jumpDown;

    /// <summary>The crouch key is held (PressCrouch puts it down), as a held key whatever the crouch setting.</summary>
    internal static bool Crouch;
    private static int crouchDown = -1;

    /// <summary>Puts the crouch key down from the next frame; it stays held until Crouch is let go.</summary>
    internal static void PressCrouch()
    {
        Crouch = true;
        crouchDown = Time.frameCount + 1;
    }

    /// <summary>The crouch key went down this frame.</summary>
    internal static bool CrouchPressed => Crouch && Time.frameCount == crouchDown;
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
        On = Sprint = Jump = Crouch = false;
        jumpDown = crouchDown = -1;
        Move = Vector2.zero;
        yaw = 0f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Off();
}
