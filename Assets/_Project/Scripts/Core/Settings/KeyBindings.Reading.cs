using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// KeyBindings: Finding and reading. One part of the class; the rest is in KeyBindings.cs and the files beside it.
public static partial class KeyBindings
{
    /// <summary>The project's action at "Map/Action", with the player's keys on it; null when the asset lacks it.</summary>
    public static InputAction Find(string path)
    {
        var actions = InputSystem.actions;
        if (actions == null || string.IsNullOrEmpty(path)) return null;
        EnsureRestored();
        if (cachedFor != actions) { found.Clear(); cachedFor = actions; }
        if (!found.TryGetValue(path, out var action) || action == null)
        {
            action = actions.FindAction(path, false);
            if (action != null) found[path] = action;
        }
        return action;
    }

    /// <summary>
    /// The action's key went down this frame. While its map is off (a menu holds the player's keys), its bound controls
    /// are read straight, so a menu's own close key still works and a caller's checks decide as they did before.
    /// </summary>
    public static bool Pressed(InputAction action)
    {
        if (action == null) return false;
        if (action.enabled) return action.WasPressedThisFrame();
        foreach (var control in action.controls)
            if (control is ButtonControl button && button.wasPressedThisFrame) return true;
        return false;
    }

    /// <summary>The action's key is down (read straight from its controls while its map is off, as <see cref="Pressed"/>).</summary>
    public static bool Held(InputAction action)
    {
        if (action == null) return false;
        if (action.enabled) return action.IsPressed();
        foreach (var control in action.controls)
            if (control is ButtonControl button && button.isPressed) return true;
        return false;
    }

    public static bool Pressed(string path) => Pressed(Find(path));

    public static bool Held(string path) => Held(Find(path));
}
