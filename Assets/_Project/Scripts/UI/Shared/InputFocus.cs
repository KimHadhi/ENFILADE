using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Whether the player's keys and clicks belong to the game right now. Every script that reads input asked this in its
/// own words; a new screen that takes the keyboard is added here once.
/// The answers come from who holds what (InputHold) and from the window having the focus, never from the pointer being
/// locked: a screen may show the pointer and leave the legs to the player (Held.Cursor without Held.Move), so "the
/// pointer is free" says nothing about walking. Three questions, one for each thing a screen can take: the body's keys
/// (TakesMove), the view (TakesLook), the gun in the hands (TakesCombat). A read of a mouse click asks TakesCombat, a read
/// of a key of the body asks TakesMove (PlayerMovement.InputAvailable is that, for a living local player). A read of
/// one key by its action asks Takes(action), which knows which of the three the action is and that a mouse button is
/// the pointer's while a pointer is out.
/// These decide what an input frame holds and whether something new may start. They are never a reason to stop what is
/// already under way: an execution, a hang or a throw ends for reasons of the game alone.
/// </summary>
public static class InputFocus
{
    /// <summary>The body's keys are the player's: the window has the focus and no screen holds them.</summary>
    public static bool TakesMove() => (Application.isFocused || ScriptedInput.On) && !InputHold.Any(Held.Move);

    /// <summary>The mouse turns the view: the window has the focus and no screen holds the view or the pointer.</summary>
    public static bool TakesLook() => (Application.isFocused || ScriptedInput.On) && !InputHold.Any(Held.Look);

    /// <summary>The trigger, the sights and the reload are the player's: no screen holds the hands or the pointer.</summary>
    public static bool TakesCombat() => Application.isFocused && !InputHold.Any(Held.Combat);

    /// <summary>The body's keys are the player's and the console is not being typed into (nor closed by this frame's key).</summary>
    public static bool TakesInput() => TakesMove() && !DevConsole.TakesKeyboard;

    /// <summary>As TakesInput, and neither the shop nor the perk cards has the keyboard.</summary>
    public static bool TakesPlayInput() => TakesInput() && !ShopMenu.TakesKeyboard && !PerkCardScreen.TakesKeyboard;

    /// <summary>
    /// This action's key is the game's to read right now: the window has the focus and no screen has the key
    /// (InputHold.Keeps: a screen holding what the action belongs to, or a pointer out over an action that sits on a
    /// mouse button). KeyBindings.Pressed and Held answer "not pressed" by this already. A move that lasts while its key
    /// is held and happens when the key is let go (a grenade held back, a stone's aim, a barricade's plan) asks it for
    /// itself, to tell the key taken from the key let go: taken, nobody chose to let go, so the move is put away and
    /// nothing happens.
    /// </summary>
    public static bool Takes(InputAction action) => action != null && Application.isFocused && !InputHold.Keeps(action);

    /// <summary>The action went down this frame; false for an action that is not there.</summary>
    public static bool Pressed(InputAction action) => action != null && action.WasPressedThisFrame();
}
