using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Whether the player's keys and clicks belong to the game right now. Every script that reads input asked this in its
/// own words; a new screen that takes the keyboard is added here once.
/// </summary>
public static class InputFocus
{
    /// <summary>The cursor is locked to the game, the window has focus and the console is not being typed into.</summary>
    public static bool TakesInput() => Cursor.lockState == CursorLockMode.Locked && Application.isFocused && !DevConsole.TakesKeyboard;

    /// <summary>As TakesInput, and neither the shop nor the perk cards has the keyboard.</summary>
    public static bool TakesPlayInput() => Cursor.lockState == CursorLockMode.Locked && Application.isFocused && !DevConsole.TakesKeyboard
        && !ShopMenu.TakesKeyboard && !PerkCardScreen.TakesKeyboard;

    /// <summary>The action went down this frame; false for an action that is not there.</summary>
    public static bool Pressed(InputAction action) => action != null && action.WasPressedThisFrame();
}
